using AiThread.Common.DTOs;
using AiThread.Common.Entities;
using AiThread.Data.Cache;
using AiThread.Data.Context;
using AiThread.Data.Messaging;
using AiThread.Service.Interfaces;
using AiThread.Service.Models;
using AiThread.Service.Workspace;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AiThread.Service.Implementations;

public sealed class ProjectService(
    IDbContextFactory<AiThreadDbContext> dbFactory,
    IShortMemoryStore shortMemoryStore,
    IEventBus eventBus,
    IActivityLogService activityLog,
    ILogger<ProjectService> logger) : IProjectService
{
    public async Task<ProjectContext?> ResolveAsync(string? workingDirectory, string? solutionHint, CancellationToken cancellationToken = default)
    {
        var directory = ExistingDirectory(workingDirectory);
        var solution = FindSolution(solutionHint) ?? (directory is null ? null : SolutionLocator.Find(directory));

        if (solution is null)
        {
            // No solution: still usable as an ad-hoc workspace, but not registered as a project.
            return directory is null ? null : new ProjectContext(null, new DirectoryInfo(directory).Name, directory, null, []);
        }

        var project = await UpsertAsync(solution, RootFor(solution, directory), cancellationToken);
        return ToContext(project);
    }

    public async Task<ProjectContext?> GetContextAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == projectId, cancellationToken);
        if (project is null) return null;

        project.LastSeenAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return ToContext(project);
    }

    public async Task<IReadOnlyList<ProjectDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var rows = await db.Projects.AsNoTracking()
            .OrderByDescending(p => p.LastSeenAt)
            .Select(p => new
            {
                Project = p,
                ThreadCount = p.Threads.Count(),
                SessionCount = p.Threads.Where(t => t.SessionId != null).Select(t => t.SessionId).Distinct().Count(),
                LongMemoryCount = db.LongMemories.Count(m => m.ProjectId == p.Id)
            })
            .ToListAsync(cancellationToken);

        var shortCounts = await ShortMemoryCountsAsync();
        return rows.Select(r => ToDto(
            r.Project, r.ThreadCount, r.SessionCount,
            r.LongMemoryCount + shortCounts.GetValueOrDefault(r.Project.Id))).ToList();
    }

    public async Task<ProjectDetailDto?> GetDetailAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var project = await db.Projects.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (project is null) return null;

        var sessions = await db.Threads.AsNoTracking()
            .Where(t => t.ProjectId == id && t.SessionId != null)
            .GroupBy(t => t.SessionId!)
            .Select(g => new ProjectSessionDto(g.Key, g.Count(), g.Min(t => t.CreatedAt), g.Max(t => t.CreatedAt)))
            .ToListAsync(cancellationToken);

        var threadCount = await db.Threads.CountAsync(t => t.ProjectId == id, cancellationToken);
        var longCount = await db.LongMemories.CountAsync(m => m.ProjectId == id, cancellationToken);
        var shortCounts = await ShortMemoryCountsAsync();

        var dto = ToDto(project, threadCount, sessions.Count, longCount + shortCounts.GetValueOrDefault(id));
        var solutionProjects = File.Exists(project.SolutionPath) ? SolutionLocator.ReadProjects(project.SolutionPath) : [];
        return new ProjectDetailDto(dto, solutionProjects, sessions.OrderByDescending(s => s.LastSeenAt).ToList());
    }

    public async Task<ProjectDto> RegisterAsync(string path, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new AppValidationException("Path wajib diisi.");

        var cleaned = path.Trim().Trim('"');
        var solution = FindSolution(cleaned) ?? (Directory.Exists(cleaned) ? SolutionLocator.Find(cleaned) : null);
        if (solution is null)
            throw new AppValidationException("File .sln/.slnx tidak ditemukan pada path tersebut.");

        var project = await UpsertAsync(solution, Path.GetDirectoryName(solution)!, cancellationToken);
        return (await GetAllAsync(cancellationToken)).First(p => p.Id == project.Id);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (project is null) return false;

        db.Projects.Remove(project);
        await db.SaveChangesAsync(cancellationToken);

        if (shortMemoryStore.IsConnected)
        {
            try
            {
                foreach (var record in (await shortMemoryStore.GetAllAsync()).Where(r => r.ProjectId == id))
                    await shortMemoryStore.DeleteAsync(record.Id);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not delete short memories of project {ProjectId}", id);
            }
        }

        eventBus.Publish(AppEvent.Create(AppEventTypes.ProjectChanged, new { id, action = "deleted" }));
        activityLog.Info("project", $"Project '{project.Name}' dihapus beserta memory-nya.", projectId: id);
        return true;
    }

    private async Task<Project> UpsertAsync(string solutionPath, string rootPath, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var project = await db.Projects.FirstOrDefaultAsync(p => p.SolutionPath == solutionPath, cancellationToken);
        if (project is not null)
        {
            project.LastSeenAt = DateTime.UtcNow;
            project.RootPath = rootPath;
            await db.SaveChangesAsync(cancellationToken);
            return project;
        }

        project = new Project
        {
            Name = Path.GetFileNameWithoutExtension(solutionPath),
            SolutionPath = solutionPath,
            RootPath = rootPath,
            LastSeenAt = DateTime.UtcNow
        };
        db.Projects.Add(project);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Two sessions of the same solution raced; the other one registered it.
            db.ChangeTracker.Clear();
            return await db.Projects.FirstAsync(p => p.SolutionPath == solutionPath, cancellationToken);
        }

        eventBus.Publish(AppEvent.Create(AppEventTypes.ProjectChanged, new { project.Id, action = "created" }));
        activityLog.Success("project", $"Project baru terdeteksi: {project.Name} ({solutionPath}).", projectId: project.Id);
        return project;
    }

    private async Task<Dictionary<Guid, int>> ShortMemoryCountsAsync()
    {
        if (!shortMemoryStore.IsConnected) return [];
        try
        {
            return (await shortMemoryStore.GetAllAsync())
                .Where(r => r.ProjectId is not null)
                .GroupBy(r => r.ProjectId!.Value)
                .ToDictionary(g => g.Key, g => g.Count());
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not read short memories from Redis.");
            return [];
        }
    }

    // The workspace root is the solution folder, unless the session was opened above it (repo root with src/*.sln).
    private static string RootFor(string solutionPath, string? workingDirectory)
    {
        var solutionDirectory = Path.GetDirectoryName(solutionPath)!;
        if (workingDirectory is null) return solutionDirectory;

        var cwd = Path.TrimEndingDirectorySeparator(workingDirectory);
        return solutionDirectory.StartsWith(cwd + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ? cwd : solutionDirectory;
    }

    private static string? FindSolution(string? hint)
    {
        if (string.IsNullOrWhiteSpace(hint)) return null;
        try
        {
            var full = Path.GetFullPath(hint.Trim().Trim('"'));
            return File.Exists(full) && SolutionLocator.IsSolutionFile(full) ? full : null;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    private static string? ExistingDirectory(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try
        {
            var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path.Trim().Trim('"')));
            return Directory.Exists(full) ? full : null;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    private static ProjectContext ToContext(Project project) => new(
        project.Id,
        project.Name,
        Directory.Exists(project.RootPath) ? project.RootPath : Path.GetDirectoryName(project.SolutionPath)!,
        project.SolutionPath,
        File.Exists(project.SolutionPath) ? SolutionLocator.ReadProjects(project.SolutionPath) : []);

    private static ProjectDto ToDto(Project p, int threadCount, int sessionCount, int memoryCount) => new(
        p.Id, p.Name, p.SolutionPath, p.RootPath, p.Summary, p.CreatedAt, p.LastSeenAt,
        threadCount, sessionCount, memoryCount, File.Exists(p.SolutionPath));
}

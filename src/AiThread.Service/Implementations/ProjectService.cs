using System.Collections.Concurrent;
using System.Diagnostics;
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
    // A session keeps its decision for a while so every prompt does not rescan the disk; ignored sessions are
    // re-checked after it expires, in case a solution was added in the meantime.
    private static readonly TimeSpan SessionDecisionLifetime = TimeSpan.FromMinutes(30);
    private const int MaxBrowseFolders = 1000;
    private const int MaxFoldersForSolutionHint = 300;
    private const int MaxScanResults = 300;
    private const int MaxScanFolders = 50_000;
    private static readonly TimeSpan ScanTimeLimit = TimeSpan.FromSeconds(20);

    private readonly ConcurrentDictionary<string, SessionDecision> _sessions = new(StringComparer.OrdinalIgnoreCase);

    public async Task<ProjectContext?> ResolveSessionAsync(string? sessionId, string? workingDirectory, string? solutionHint, CancellationToken cancellationToken = default)
    {
        var directory = ExistingDirectory(workingDirectory);
        var key = $"{sessionId}|{directory}|{solutionHint}";
        var now = DateTime.UtcNow;

        if (_sessions.TryGetValue(key, out var decision) && decision.ExpiresAt > now)
        {
            // Ignored sessions keep a fixed expiry so an active session is re-checked periodically.
            if (decision.ProjectId is null) return null;

            // The project may have been deleted from the Project menu since; fall through and resolve again.
            if (await GetContextAsync(decision.ProjectId.Value, cancellationToken) is { } cached)
            {
                _sessions[key] = decision with { ExpiresAt = now + SessionDecisionLifetime };
                return cached;
            }
        }

        PruneExpiredSessions(now);

        var solution = FindSolution(solutionHint) ?? (directory is null ? null : SolutionLocator.Find(directory));
        var context = solution is null ? null : ToContext(await UpsertAsync(solution, RootFor(solution, directory), cancellationToken));
        _sessions[key] = new SessionDecision(context?.ProjectId, now + SessionDecisionLifetime);

        var sessionLabel = string.IsNullOrEmpty(sessionId) ? "tanpa id" : sessionId[..Math.Min(8, sessionId.Length)];
        if (context is null)
        {
            activityLog.Info("proxy",
                $"Sesi {sessionLabel} ({directory ?? "tanpa working directory"}) bukan bagian project .sln/.slnx — prompt diabaikan orkestrasi dan diteruskan langsung.");
        }
        else
        {
            activityLog.Info("proxy", $"Sesi {sessionLabel} terhubung ke project {context.Name}.", projectId: context.ProjectId);
        }

        return context;
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
        var solution = FindSolution(cleaned) ?? (ExistingDirectory(cleaned) is { } directory ? SolutionLocator.Find(directory) : null);
        if (solution is null)
            throw new AppValidationException("File .sln/.slnx tidak ditemukan pada path tersebut. Gunakan tombol Telusuri untuk mencarinya.");

        var project = await UpsertAsync(solution, Path.GetDirectoryName(solution)!, cancellationToken);
        return (await GetAllAsync(cancellationToken)).First(p => p.Id == project.Id);
    }

    public async Task<RegisterProjectsResultDto> RegisterManyAsync(IReadOnlyList<string> solutionPaths, CancellationToken cancellationToken = default)
    {
        if (solutionPaths is not { Count: > 0 }) throw new AppValidationException("Pilih minimal satu solution.");

        var registered = await RegisteredSolutionsAsync(cancellationToken);
        int added = 0, existing = 0;
        var errors = new List<string>();

        foreach (var path in solutionPaths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var solution = FindSolution(path);
            if (solution is null)
            {
                errors.Add($"{path}: bukan file .sln/.slnx yang ada.");
                continue;
            }
            if (registered.ContainsKey(solution))
            {
                existing++;
                continue;
            }

            await UpsertAsync(solution, Path.GetDirectoryName(solution)!, cancellationToken);
            added++;
        }

        return new RegisterProjectsResultDto(added, existing, errors);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (project is null) return false;

        db.Projects.Remove(project);
        await db.SaveChangesAsync(cancellationToken);

        foreach (var (key, decision) in _sessions)
            if (decision.ProjectId == id) _sessions.TryRemove(key, out _);

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

    public async Task<FolderBrowseDto> BrowseAsync(string? path, CancellationToken cancellationToken = default)
    {
        var quickLocations = await QuickLocationsAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(path)) return new FolderBrowseDto(null, null, Drives(), [], quickLocations);

        // A solution file path opens its folder.
        var directory = ExistingDirectory(path)
                        ?? (FindSolution(path) is { } solutionFile ? Path.GetDirectoryName(solutionFile) : null)
                        ?? throw new AppValidationException($"Folder '{path}' tidak ditemukan.");

        var info = new DirectoryInfo(directory);
        var children = SolutionLocator.BrowsableDirectories(info).Take(MaxBrowseFolders).ToList();
        var markSolutions = children.Count <= MaxFoldersForSolutionHint;
        var folders = children
            .Select(child => new FolderEntryDto(child.Name, child.FullName, markSolutions && SolutionLocator.SolutionsIn(child).Count > 0))
            .ToList();

        var registered = await RegisteredSolutionsAsync(cancellationToken);
        var solutions = SolutionLocator.SolutionsIn(info).Select(s => ToSolutionDto(s, registered)).ToList();
        return new FolderBrowseDto(info.FullName, info.Parent?.FullName, folders, solutions, quickLocations);
    }

    public async Task<SolutionScanDto> ScanAsync(string path, int depth, CancellationToken cancellationToken = default)
    {
        var directory = ExistingDirectory(path) ?? throw new AppValidationException($"Folder '{path}' tidak ditemukan.");
        depth = Math.Clamp(depth, 1, 8);

        using var timeLimit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeLimit.CancelAfter(ScanTimeLimit);
        var stopwatch = Stopwatch.StartNew();
        var result = await Task.Run(() => SolutionLocator.Scan(directory, depth, MaxScanResults, MaxScanFolders, timeLimit.Token), cancellationToken);
        stopwatch.Stop();

        var registered = await RegisteredSolutionsAsync(cancellationToken);
        return new SolutionScanDto(
            directory,
            depth,
            result.Solutions.Select(s => ToSolutionDto(s, registered)).ToList(),
            result.FoldersScanned,
            result.Truncated,
            stopwatch.ElapsedMilliseconds);
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
        activityLog.Success("project", $"Project baru terdaftar: {project.Name} ({solutionPath}).", projectId: project.Id);
        return project;
    }

    private async Task<Dictionary<string, Guid>> RegisteredSolutionsAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var rows = await db.Projects.AsNoTracking().Select(p => new { p.SolutionPath, p.Id }).ToListAsync(cancellationToken);
        return rows.ToDictionary(r => r.SolutionPath, r => r.Id, StringComparer.OrdinalIgnoreCase);
    }

    // Drives, the user's home and source\repos, and the folders around recently used projects.
    private async Task<IReadOnlyList<FolderEntryDto>> QuickLocationsAsync(CancellationToken cancellationToken)
    {
        var locations = new List<FolderEntryDto>(Drives());

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (Directory.Exists(home)) locations.Add(new FolderEntryDto("Home", home, false));
        var repos = Path.Combine(home, "source", "repos");
        if (Directory.Exists(repos)) locations.Add(new FolderEntryDto("source\\repos", repos, false));

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var recentRoots = await db.Projects.AsNoTracking()
            .OrderByDescending(p => p.LastSeenAt)
            .Select(p => p.RootPath)
            .Take(20)
            .ToListAsync(cancellationToken);

        foreach (var parent in recentRoots
                     .Select(Path.GetDirectoryName)
                     .Where(p => !string.IsNullOrEmpty(p) && Directory.Exists(p))
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .Take(6))
        {
            if (locations.Any(l => string.Equals(l.Path, parent, StringComparison.OrdinalIgnoreCase))) continue;
            locations.Add(new FolderEntryDto(Path.GetFileName(parent!) is { Length: > 0 } name ? name : parent!, parent!, false));
        }

        return locations;
    }

    private static List<FolderEntryDto> Drives()
    {
        var drives = new List<FolderEntryDto>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (!drive.IsReady || drive.DriveType is not (DriveType.Fixed or DriveType.Network or DriveType.Removable)) continue;
                var label = string.IsNullOrWhiteSpace(drive.VolumeLabel) ? drive.Name : $"{drive.Name} {drive.VolumeLabel}";
                drives.Add(new FolderEntryDto(label, drive.RootDirectory.FullName, false));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Drive disappeared or is locked — skip it.
            }
        }
        return drives;
    }

    private void PruneExpiredSessions(DateTime now)
    {
        if (_sessions.Count < 500) return;
        foreach (var (key, decision) in _sessions)
            if (decision.ExpiresAt <= now) _sessions.TryRemove(key, out _);
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

    private static SolutionFileDto ToSolutionDto(string solutionPath, Dictionary<string, Guid> registered) => new(
        Path.GetFileName(solutionPath),
        solutionPath,
        Path.GetDirectoryName(solutionPath)!,
        registered.ContainsKey(solutionPath),
        registered.TryGetValue(solutionPath, out var id) ? id : null);

    private static ProjectContext ToContext(Project project) => new(
        project.Id,
        project.Name,
        Directory.Exists(project.RootPath) ? project.RootPath : Path.GetDirectoryName(project.SolutionPath)!,
        project.SolutionPath,
        File.Exists(project.SolutionPath) ? SolutionLocator.ReadProjects(project.SolutionPath) : []);

    private static ProjectDto ToDto(Project p, int threadCount, int sessionCount, int memoryCount) => new(
        p.Id, p.Name, p.SolutionPath, p.RootPath, p.Summary, p.CreatedAt, p.LastSeenAt,
        threadCount, sessionCount, memoryCount, File.Exists(p.SolutionPath));

    private sealed record SessionDecision(Guid? ProjectId, DateTime ExpiresAt);
}

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
    // re-checked after a few seconds, in case a marker (.git, a manifest) was added in the meantime.
    private static readonly TimeSpan SessionDecisionLifetime = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan IgnoredSessionLifetime = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ProfileLifetime = TimeSpan.FromSeconds(30);
    private const int MaxBrowseFolders = 1000;
    private const int MaxFoldersForProjectHint = 300;
    private const int MaxScanResults = 300;
    private const int MaxScanFolders = 50_000;
    private static readonly TimeSpan ScanTimeLimit = TimeSpan.FromSeconds(20);

    private readonly ConcurrentDictionary<string, SessionDecision> _sessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, ProjectProfile> _profiles = new(StringComparer.OrdinalIgnoreCase);

    public async Task<ProjectContext?> ResolveSessionAsync(string? sessionId, string? workingDirectory, string? projectHint, CancellationToken cancellationToken = default)
    {
        var directory = ExistingDirectory(workingDirectory);
        var hint = FolderOf(projectHint);
        var key = $"{sessionId}|{directory}|{hint}";
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

        // An explicit hint (header) wins; one that leads nowhere falls back to the working directory.
        var lookup = await LookupAsync(hint, cancellationToken);
        if (lookup.Project is null && !string.Equals(directory, hint, StringComparison.OrdinalIgnoreCase))
            lookup = await LookupAsync(directory, cancellationToken);

        var context = lookup.Project is null ? null : ToContext(lookup.Project);
        _sessions[key] = new SessionDecision(context?.ProjectId, now + (context is null ? IgnoredSessionLifetime : SessionDecisionLifetime));

        var sessionLabel = string.IsNullOrEmpty(sessionId) ? "tanpa id" : sessionId[..Math.Min(8, sessionId.Length)];
        if (context is null)
        {
            var subject = directory ?? hint;
            activityLog.Info("proxy",
                $"Sesi {sessionLabel} ({subject ?? "tanpa working directory"}) bukan bagian project ({NoProjectReason(subject, lookup.Resolution)}) — prompt diabaikan orkestrasi dan diteruskan langsung.");
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
        return new ProjectDetailDto(dto, ProfileOf(project.RootPath).Modules, sessions.OrderByDescending(s => s.LastSeenAt).ToList());
    }

    public async Task<ProjectDto> RegisterAsync(string path, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new AppValidationException("Path wajib diisi.");

        var root = RootForRegistration(path);
        var project = await UpsertAsync(root.Path, root.Name, cancellationToken);
        return (await GetAllAsync(cancellationToken)).First(p => p.Id == project.Id);
    }

    public async Task<RegisterProjectsResultDto> RegisterManyAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken = default)
    {
        if (paths is not { Count: > 0 }) throw new AppValidationException("Pilih minimal satu project.");

        var registered = await RegisteredRootsAsync(cancellationToken);
        int added = 0, existing = 0;
        var errors = new List<string>();

        foreach (var path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            ProjectRoot root;
            try
            {
                root = RootForRegistration(path);
            }
            catch (AppValidationException ex)
            {
                errors.Add($"{path}: {ex.Message}");
                continue;
            }

            if (registered.ContainsKey(root.Path))
            {
                existing++;
                continue;
            }

            var project = await UpsertAsync(root.Path, root.Name, cancellationToken);
            registered[root.Path] = project.Id;
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

        // A file path (e.g. a solution) opens its folder.
        var directory = FolderOf(path) ?? throw new AppValidationException($"Folder '{path}' tidak ditemukan.");

        var info = new DirectoryInfo(directory);
        var children = ProjectLocator.BrowsableDirectories(info).Take(MaxBrowseFolders).ToList();
        var markProjects = children.Count <= MaxFoldersForProjectHint;
        var folders = children
            .Select(child => markProjects && ProjectLocator.Inspect(child.FullName) is { } root
                ? new FolderEntryDto(child.Name, child.FullName, true, root.Stacks)
                : new FolderEntryDto(child.Name, child.FullName, false, []))
            .ToList();

        var registered = await RegisteredRootsAsync(cancellationToken);
        var projects = new List<ProjectCandidateDto>();
        if (!ProjectLocator.IsBlockedRoot(info.FullName) && ProjectLocator.Inspect(info.FullName) is { } own)
            projects.Add(ToCandidateDto(own, registered));
        return new FolderBrowseDto(info.FullName, info.Parent?.FullName, folders, projects, quickLocations);
    }

    public async Task<ProjectScanDto> ScanAsync(string path, int depth, CancellationToken cancellationToken = default)
    {
        var directory = ExistingDirectory(path) ?? throw new AppValidationException($"Folder '{path}' tidak ditemukan.");
        depth = Math.Clamp(depth, 1, 8);

        using var timeLimit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeLimit.CancelAfter(ScanTimeLimit);
        var stopwatch = Stopwatch.StartNew();
        var result = await Task.Run(() => ProjectLocator.Scan(directory, depth, MaxScanResults, MaxScanFolders, timeLimit.Token), cancellationToken);
        stopwatch.Stop();

        var registered = await RegisteredRootsAsync(cancellationToken);
        return new ProjectScanDto(
            directory,
            depth,
            result.Projects.Select(p => ToCandidateDto(p, registered)).ToList(),
            result.FoldersScanned,
            result.Truncated,
            stopwatch.ElapsedMilliseconds);
    }

    // A registered project that contains the folder keeps its identity wherever the locator would put the root today
    // (projects registered by hand, or by an earlier version that identified them by their .sln file).
    private async Task<(Project? Project, ProjectResolution? Resolution)> LookupAsync(string? start, CancellationToken cancellationToken)
    {
        if (start is null) return (null, null);

        Project? registered;
        await using (var db = await dbFactory.CreateDbContextAsync(cancellationToken))
        {
            registered = (await db.Projects.AsNoTracking().ToListAsync(cancellationToken))
                .Where(p => IsSameOrInside(start, p.RootPath) && Directory.Exists(p.RootPath))
                .OrderByDescending(p => p.RootPath.Length)
                .FirstOrDefault();
        }
        if (registered is not null) return (await UpsertAsync(registered.RootPath, registered.Name, cancellationToken), null);

        var resolution = ProjectLocator.Resolve(start);
        var project = resolution.Root is { } root ? await UpsertAsync(root.Path, root.Name, cancellationToken) : null;
        return (project, resolution);
    }

    // Registering a folder by hand is explicit: the folder's own markers win, then the project it belongs to, and a
    // folder without any marker is taken as it is (only drives, home and system folders are refused).
    private static ProjectRoot RootForRegistration(string path)
    {
        var directory = FolderOf(path.Trim().Trim('"')) ?? throw new AppValidationException($"Folder '{path}' tidak ditemukan.");
        if (ProjectLocator.IsBlockedRoot(directory))
            throw new AppValidationException("Folder ini terlalu luas untuk jadi project (drive, home, atau folder sistem). Pilih folder project-nya.");

        return ProjectLocator.Inspect(directory) ?? ProjectLocator.Resolve(directory).Root ?? ProjectLocator.Plain(directory);
    }

    private async Task<Project> UpsertAsync(string rootPath, string name, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var project = await db.Projects.FirstOrDefaultAsync(p => p.RootPath == rootPath, cancellationToken);
        if (project is not null)
        {
            project.LastSeenAt = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            return project;
        }

        project = new Project
        {
            Name = name,
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
            // Two sessions of the same project raced; the other one registered it.
            db.ChangeTracker.Clear();
            return await db.Projects.FirstAsync(p => p.RootPath == rootPath, cancellationToken);
        }

        eventBus.Publish(AppEvent.Create(AppEventTypes.ProjectChanged, new { project.Id, action = "created" }));
        activityLog.Success("project", $"Project baru terdaftar: {project.Name} ({rootPath}).", projectId: project.Id);
        return project;
    }

    private async Task<Dictionary<string, Guid>> RegisteredRootsAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var rows = await db.Projects.AsNoTracking().Select(p => new { p.RootPath, p.Id }).ToListAsync(cancellationToken);
        return rows.ToDictionary(r => r.RootPath, r => r.Id, StringComparer.OrdinalIgnoreCase);
    }

    // Drives, the user's home and source\repos, and the folders around recently used projects.
    private async Task<IReadOnlyList<FolderEntryDto>> QuickLocationsAsync(CancellationToken cancellationToken)
    {
        var locations = new List<FolderEntryDto>(Drives());

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (Directory.Exists(home)) locations.Add(new FolderEntryDto("Home", home, false, []));
        var repos = Path.Combine(home, "source", "repos");
        if (Directory.Exists(repos)) locations.Add(new FolderEntryDto("source\\repos", repos, false, []));

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
            locations.Add(new FolderEntryDto(Path.GetFileName(parent!) is { Length: > 0 } name ? name : parent!, parent!, false, []));
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
                drives.Add(new FolderEntryDto(label, drive.RootDirectory.FullName, false, []));
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

    // Stacks and modules come from the manifests on disk; a short cache keeps the project list and every new thread
    // from walking the folder tree again.
    private ProjectProfile ProfileOf(string rootPath)
    {
        var now = DateTime.UtcNow;
        if (_profiles.TryGetValue(rootPath, out var cached) && cached.ExpiresAt > now) return cached;

        var manifests = ProjectLocator.FindManifests(rootPath);
        var profile = new ProjectProfile(ProjectLocator.StacksOf(manifests), manifests.Select(m => m.RelativePath).ToList(), now + ProfileLifetime);
        _profiles[rootPath] = profile;
        return profile;
    }

    // What a session outside any project looks like in the log, so the user knows what to change.
    private static string NoProjectReason(string? subject, ProjectResolution? resolution)
    {
        if (subject is null) return "working directory tidak diketahui";
        if (resolution?.Nearby is { Count: > 0 } nearby)
            return $"folder ini berisi project {string.Join(", ", nearby.Take(5).Select(p => p.Name))}{(nearby.Count > 5 ? ", …" : string.Empty)}; buka sesi di salah satunya";
        if (ProjectLocator.IsBlockedRoot(subject)) return "folder ini terlalu luas untuk jadi project (drive, home, atau folder sistem)";
        return "tidak ada marker project seperti .git, .sln, pom.xml, atau package.json di folder ini maupun folder induknya";
    }

    private static bool IsSameOrInside(string path, string root)
    {
        var trimmed = Path.TrimEndingDirectorySeparator(root);
        return path.Equals(trimmed, StringComparison.OrdinalIgnoreCase)
               || path.StartsWith(trimmed + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    // A directory, or the folder of a file (the header used to carry the .sln path).
    private static string? FolderOf(string? path)
    {
        if (ExistingDirectory(path) is { } directory) return directory;
        if (string.IsNullOrWhiteSpace(path)) return null;
        try
        {
            var full = Path.GetFullPath(path.Trim().Trim('"'));
            return File.Exists(full) ? Path.GetDirectoryName(full) : null;
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
            var cleaned = path.Trim().Trim('`', '"', '\'');
            var wasIndex = cleaned.IndexOf("(was", StringComparison.OrdinalIgnoreCase);
            if (wasIndex >= 0) cleaned = cleaned[..wasIndex].Trim().Trim('`', '"', '\'');
            var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(cleaned));
            return Directory.Exists(full) ? full : null;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    private static ProjectCandidateDto ToCandidateDto(ProjectRoot root, Dictionary<string, Guid> registered) => new(
        root.Name,
        root.Path,
        root.Stacks,
        registered.ContainsKey(root.Path),
        registered.TryGetValue(root.Path, out var id) ? id : null);

    private ProjectContext ToContext(Project project)
    {
        var profile = ProfileOf(project.RootPath);
        return new ProjectContext(project.Id, project.Name, project.RootPath, profile.Stacks, profile.Modules);
    }

    private ProjectDto ToDto(Project p, int threadCount, int sessionCount, int memoryCount) => new(
        p.Id, p.Name, p.RootPath, ProfileOf(p.RootPath).Stacks, p.Summary, p.CreatedAt, p.LastSeenAt,
        threadCount, sessionCount, memoryCount, Directory.Exists(p.RootPath));

    private sealed record SessionDecision(Guid? ProjectId, DateTime ExpiresAt);

    private sealed record ProjectProfile(IReadOnlyList<string> Stacks, IReadOnlyList<string> Modules, DateTime ExpiresAt);
}

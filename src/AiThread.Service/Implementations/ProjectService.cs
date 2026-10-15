using System.Collections.Concurrent;
using System.Diagnostics;
using AiThread.Common.DTOs;
using AiThread.Common.Entities;
using AiThread.Common.Enums;
using AiThread.Data.Cache;
using AiThread.Data.Context;
using AiThread.Data.Memory;
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
    LongMemoryStore longMemoryStore,
    ProjectMemoryKeys memoryKeys,
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
                SessionCount = p.Threads.Where(t => t.SessionId != null).Select(t => t.SessionId).Distinct().Count()
            })
            .ToListAsync(cancellationToken);

        var counts = await MemoryCountsAsync(rows.Select(r => r.Project).ToList(), null, cancellationToken);
        return rows.Select(r => ToDto(r.Project, r.ThreadCount, r.SessionCount, counts.GetValueOrDefault(r.Project.Id))).ToList();
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
        var counts = await MemoryCountsAsync([project], project.ProjectKey, cancellationToken);

        var dto = ToDto(project, threadCount, sessions.Count, counts.GetValueOrDefault(id));
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

    public async Task<bool> DeleteAsync(Guid id, bool includeSharedMemory = false, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (project is null) return false;

        // Another clone of the same repository is still registered here: the memory stays with it.
        var key = project.ProjectKey;
        var sibling = string.IsNullOrEmpty(key)
            ? null
            : await db.Projects.Where(p => p.Id != id && p.ProjectKey == key).Select(p => (Guid?)p.Id).FirstOrDefaultAsync(cancellationToken);
        if (sibling is { } siblingId)
        {
            try
            {
                await memoryKeys.HandOverAsync(id, siblingId, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Could not hand the long memories of project {ProjectId} over to {SiblingId}", id, siblingId);
            }
        }

        db.Projects.Remove(project);
        await db.SaveChangesAsync(cancellationToken);

        foreach (var (sessionKey, decision) in _sessions)
            if (decision.ProjectId == id) _sessions.TryRemove(sessionKey, out _);

        string outcome;
        if (sibling is not null)
        {
            outcome = "memory-nya tetap dipakai clone lain dari repository yang sama";
        }
        else
        {
            await memoryKeys.DeleteAsync(id, key, includeSharedMemory, cancellationToken);
            var sharedKept = !includeSharedMemory && !ProjectKeyResolver.IsLocal(key)
                             && (memoryKeys.LongStoreIsShared || memoryKeys.ShortStoreIsShared);
            outcome = sharedKept ? "memory di store central tetap disimpan untuk developer lain" : "beserta memory-nya";
        }

        eventBus.Publish(AppEvent.Create(AppEventTypes.ProjectChanged, new { id, action = "deleted" }));
        eventBus.Publish(AppEvent.Create(AppEventTypes.MemoryChanged, new { id = (Guid?)null, action = "deleted" }));
        activityLog.Info("project", $"Project '{project.Name}' dihapus — {outcome}.", projectId: id);
        return true;
    }

    public async Task<ProjectDto?> SetKeyAsync(Guid id, string? key, CancellationToken cancellationToken = default)
    {
        if (key?.Trim().Length > ProjectKeyResolver.MaxKeyLength)
            throw new AppValidationException($"Project key maksimal {ProjectKeyResolver.MaxKeyLength} karakter.");
        var manualKey = ProjectKeyResolver.NormalizeKey(key);
        if (ProjectKeyResolver.IsLocal(manualKey))
            throw new AppValidationException($"Project key tidak boleh diawali '{ProjectKeyResolver.LocalPrefix}'.");

        await using (var db = await dbFactory.CreateDbContextAsync(cancellationToken))
        {
            var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
            if (project is null) return null;

            project.ManualKey = manualKey;
            var previous = ApplyKey(project);
            project.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            await OnKeyChangedAsync(project, previous, cancellationToken);

            if (manualKey is not null && project.ProjectKeySource == ProjectKeySource.File)
                activityLog.Warn("project", $"Key manual '{manualKey}' untuk project {project.Name} disimpan, tetapi {ProjectKeyResolver.OverrideFile} di repository tetap dipakai ('{project.ProjectKey}').", projectId: id);
            else
                activityLog.Info("project", $"Project key {project.Name}: {project.ProjectKey} ({KeySourceName(project.ProjectKeySource)}).", projectId: id);
        }

        eventBus.Publish(AppEvent.Create(AppEventTypes.ProjectChanged, new { id, action = "updated" }));
        return (await GetAllAsync(cancellationToken)).FirstOrDefault(p => p.Id == id);
    }

    public async Task RefreshKeysAsync(CancellationToken cancellationToken = default)
    {
        List<Project> projects;
        var changed = new List<(Project Project, string Previous)>();
        await using (var db = await dbFactory.CreateDbContextAsync(cancellationToken))
        {
            projects = await db.Projects.ToListAsync(cancellationToken);
            foreach (var project in projects)
            {
                var previous = ApplyKey(project);
                if (previous != project.ProjectKey) changed.Add((project, previous));
            }
            if (changed.Count > 0) await db.SaveChangesAsync(cancellationToken);
        }

        foreach (var (project, previous) in changed) await OnKeyChangedAsync(project, previous, cancellationToken);
        var filled = await memoryKeys.BackfillAsync(projects, cancellationToken);
        if (filled > 0)
        {
            activityLog.Info("memory", $"{filled} memory lama diberi project key agar bisa dibagi antar developer.");
            eventBus.Publish(AppEvent.Create(AppEventTypes.MemoryChanged, new { id = (Guid?)null, action = "updated" }));
        }
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
            var previous = ApplyKey(project);
            await db.SaveChangesAsync(cancellationToken);
            await OnKeyChangedAsync(project, previous, cancellationToken);
            return project;
        }

        project = new Project
        {
            Name = name,
            RootPath = rootPath,
            LastSeenAt = DateTime.UtcNow
        };
        ApplyKey(project);
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

    // Memory per project: everything under its key (from any developer) plus older rows that only carry its id.
    // Long memory may live in another database, so an outage there only hides that part of the count.
    private async Task<Dictionary<Guid, int>> MemoryCountsAsync(IReadOnlyList<Project> projects, string? onlyKey, CancellationToken cancellationToken)
    {
        var byKey = new Dictionary<string, int>();
        var byId = new Dictionary<Guid, int>();
        void Count(string? key, Guid? id)
        {
            if (key is not null) byKey[key] = byKey.GetValueOrDefault(key) + 1;
            else if (id is { } value) byId[value] = byId.GetValueOrDefault(value) + 1;
        }

        if (shortMemoryStore.IsConnected)
        {
            try
            {
                foreach (var record in await shortMemoryStore.GetAllAsync()) Count(record.ProjectKey, record.ProjectId);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not read short memories.");
            }
        }

        try
        {
            await using var db = await longMemoryStore.CreateAsync(cancellationToken);
            var query = db.LongMemories.AsNoTracking().Where(m => m.ProjectKey != null || m.ProjectId != null);
            if (onlyKey is { Length: > 0 })
            {
                var ids = projects.Select(p => p.Id).ToList();
                query = query.Where(m => m.ProjectKey == onlyKey || (m.ProjectKey == null && m.ProjectId.HasValue && ids.Contains(m.ProjectId.Value)));
            }
            var groups = await query
                .GroupBy(m => new { m.ProjectKey, m.ProjectId })
                .Select(g => new { g.Key.ProjectKey, g.Key.ProjectId, Count = g.Count() })
                .ToListAsync(cancellationToken);
            foreach (var group in groups)
            {
                if (group.ProjectKey is not null) byKey[group.ProjectKey] = byKey.GetValueOrDefault(group.ProjectKey) + group.Count;
                else if (group.ProjectId is { } id) byId[id] = byId.GetValueOrDefault(id) + group.Count;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogStoreWarning(ex, "Could not count long memories");
        }

        return projects.ToDictionary(
            p => p.Id,
            p => (string.IsNullOrEmpty(p.ProjectKey) ? 0 : byKey.GetValueOrDefault(p.ProjectKey)) + byId.GetValueOrDefault(p.Id));
    }

    // Recomputes the project's key from its folder; returns the previous key. A folder that is gone keeps its key.
    private static string ApplyKey(Project project)
    {
        var previous = project.ProjectKey;
        if (!Directory.Exists(project.RootPath) && !string.IsNullOrEmpty(previous)) return previous;

        var info = ProjectKeyResolver.Resolve(project.RootPath, project.ManualKey);
        project.ProjectKey = info.Key;
        project.ProjectKeySource = info.Source;
        return previous;
    }

    private async Task OnKeyChangedAsync(Project project, string previous, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(previous) || previous == project.ProjectKey) return;

        var moved = await memoryKeys.RekeyAsync(previous, project.ProjectKey, project.Name, cancellationToken);
        activityLog.Info("project",
            $"Project key {project.Name} berubah: {previous} → {project.ProjectKey} ({KeySourceName(project.ProjectKeySource)})"
            + (moved > 0 ? $"; {moved} memory ikut dipindah." : "."),
            projectId: project.Id);
        if (moved > 0) eventBus.Publish(AppEvent.Create(AppEventTypes.MemoryChanged, new { id = (Guid?)null, action = "updated" }));
    }

    private static string KeySourceName(ProjectKeySource source) => source switch
    {
        ProjectKeySource.File => ProjectKeyResolver.OverrideFile,
        ProjectKeySource.Manual => "key manual",
        ProjectKeySource.GitRemote => "git remote",
        _ => "lokal, belum bisa dibagi"
    };

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
        return new ProjectContext(project.Id, project.Name, project.RootPath, profile.Stacks, profile.Modules, project.ProjectKey);
    }

    private ProjectDto ToDto(Project p, int threadCount, int sessionCount, int memoryCount) => new(
        p.Id, p.Name, p.RootPath, ProfileOf(p.RootPath).Stacks, p.Summary, p.CreatedAt, p.LastSeenAt,
        threadCount, sessionCount, memoryCount, Directory.Exists(p.RootPath), p.ProjectKey, p.ProjectKeySource, p.ManualKey);

    private sealed record SessionDecision(Guid? ProjectId, DateTime ExpiresAt);

    private sealed record ProjectProfile(IReadOnlyList<string> Stacks, IReadOnlyList<string> Modules, DateTime ExpiresAt);
}

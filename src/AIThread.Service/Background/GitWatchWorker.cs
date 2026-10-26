using AIThread.Common.Enums;
using AIThread.Data.Context;
using AIThread.Service.Git;
using AIThread.Service.Interfaces;
using AIThread.Service.Workspace;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.Background;

// Keeps the git state of AIT threads current (GitChangeTracker). Runs the tracker's queue (a thread finished, git of a
// project moved) and watches the .git folder of every project that has threads being followed: a commit, checkout,
// reset, stash, merge or fetch touches HEAD, the index, refs or the reflog, and the project is looked at again a moment
// later. Edits in the working tree do not touch .git, so the projects are also looked at every few minutes. The remotes
// of the projects in use are fetched every Project.GitFetchMinutes (default 15), one project at a time, so other
// developers' pushes and merges show too; after a fetch the environments are checked and, where turned on, the commits
// made outside AIT are summarized (CommitIngestor).
public sealed class GitWatchWorker(
    IDbContextFactory<AIThreadDbContext> dbFactory,
    IGitChangeTracker tracker,
    IGitEnvironmentTracker environments,
    ICommitIngestor ingestor,
    ILogger<GitWatchWorker> logger) : BackgroundService
{
    private static readonly TimeSpan SyncInterval = TimeSpan.FromMinutes(1);
    private const int SweepEveryTicks = 5;
    private static readonly TimeSpan Debounce = TimeSpan.FromSeconds(2);

    // Names under .git whose change means the branch, the index or a ref moved.
    private static readonly string[] WatchedFiles = ["HEAD", "ORIG_HEAD", "index", "packed-refs", "FETCH_HEAD"];

    private readonly Dictionary<Guid, ProjectWatch> _watches = [];

    // Released to look at the projects before the next tick.
    private readonly SemaphoreSlim _syncNow = new(0);

    private sealed class ProjectWatch(string root, IReadOnlyList<FileSystemWatcher> watchers, Timer debounce) : IDisposable
    {
        public string Root => root;

        public void Touched() => debounce.Change(Debounce, Timeout.InfiniteTimeSpan);

        public void Dispose()
        {
            foreach (var watcher in watchers) watcher.Dispose();
            debounce.Dispose();
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.WhenAll(RunQueueAsync(stoppingToken), WatchAsync(stoppingToken), FetchLoopAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down; states are worked out again on the next start.
        }
        finally
        {
            foreach (var watch in _watches.Values) watch.Dispose();
            _watches.Clear();
        }
    }

    private async Task RunQueueAsync(CancellationToken stoppingToken)
    {
        while (await tracker.Reader.WaitToReadAsync(stoppingToken))
        {
            var waiting = new List<GitTrackRequest>();
            while (tracker.Reader.TryRead(out var request)) waiting.Add(request);

            var projects = waiting.Where(r => r.ThreadId is null).Select(r => r.ProjectId).Distinct().ToList();
            // A thread of a project that is refreshed anyway needs no turn of its own.
            var threads = waiting.Where(r => r.ThreadId is not null && !projects.Contains(r.ProjectId)).Select(r => r.ThreadId!.Value).Distinct().ToList();
            try
            {
                if (threads.Count > 0) await tracker.RefreshThreadsAsync(threads, stoppingToken);
                foreach (var project in projects) await tracker.RefreshProjectAsync(project, stoppingToken);
                // A finished thread of a project not watched yet starts its watch now rather than at the next tick.
                if (waiting.Any(r => r.ThreadId is not null && !_watches.ContainsKey(r.ProjectId))) _syncNow.Release();
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Following the git state of AIT threads failed");
            }
        }
    }

    private async Task WatchAsync(CancellationToken stoppingToken)
    {
        var tick = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var projects = await FollowedProjectsAsync(stoppingToken);
                await SyncWatchesAsync(projects, stoppingToken);
                if (tick++ % SweepEveryTicks == SweepEveryTicks - 1)
                    foreach (var project in projects.Keys)
                    {
                        tracker.ProjectChanged(project);
                        // Manual wiki edits and changes seen by other developers' AIT reach the environment pages here too.
                        await environments.RefreshAsync(project, stoppingToken);
                        // Commits pulled by hand (or with the automatic fetch off) are summarized too, when turned on.
                        await ingestor.RunAsync(project, stoppingToken);
                    }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Watching the git folders of the projects failed");
            }
            await _syncNow.WaitAsync(SyncInterval, stoppingToken);
        }
    }

    private async Task FetchLoopAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            // Not right at start: AIT starts with the machine, before the network may be up.
            await Task.Delay(SyncInterval, stoppingToken);
            try
            {
                foreach (var (id, minutes) in await FetchableProjectsAsync(stoppingToken))
                {
                    if (tracker.LastFetch(id) is { } last && DateTime.UtcNow - last.At < TimeSpan.FromMinutes(minutes)) continue;
                    // What arrived with the fetch may have reached an environment.
                    if (await tracker.FetchAsync(id, stoppingToken) is not { Error: null }) continue;
                    await environments.RefreshAsync(id, stoppingToken);
                    // Does nothing unless commit ingest is turned on for the project key.
                    await ingestor.RunAsync(id, stoppingToken);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Fetching the remotes of the projects failed");
            }
        }
    }

    // Projects in use (seen or with a followed thread within TrackedFor) whose automatic fetch is on, with its interval.
    private async Task<IReadOnlyList<(Guid Id, int Minutes)>> FetchableProjectsAsync(CancellationToken cancellationToken)
    {
        var since = DateTime.UtcNow - GitChangeTracker.TrackedFor;
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var projects = await db.Projects.AsNoTracking()
            .Where(p => p.GitFetchMinutes != 0
                        && (p.LastSeenAt >= since || p.Threads.Any(t => t.GitCheckedAt != null && (t.CompletedAt ?? t.CreatedAt) >= since)))
            .Select(p => new { p.Id, p.RootPath, p.GitFetchMinutes })
            .ToListAsync(cancellationToken);
        return projects
            .Where(p => Directory.Exists(p.RootPath) && GitTools.IsRepository(p.RootPath))
            .Select(p => (p.Id, p.GitFetchMinutes ?? GitChangeTracker.DefaultFetchMinutes))
            .ToList();
    }

    // Projects with a thread the tracker follows (looked at before, finished within TrackedFor), by id with their root.
    private async Task<Dictionary<Guid, string>> FollowedProjectsAsync(CancellationToken cancellationToken)
    {
        var since = DateTime.UtcNow - GitChangeTracker.TrackedFor;
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.Projects.AsNoTracking()
            .Where(p => p.Threads.Any(t => t.GitCheckedAt != null && t.Status != ThreadStatus.RolledBack && (t.CompletedAt ?? t.CreatedAt) >= since))
            .Select(p => new { p.Id, p.RootPath })
            .ToDictionaryAsync(p => p.Id, p => p.RootPath, cancellationToken);
    }

    private async Task SyncWatchesAsync(Dictionary<Guid, string> projects, CancellationToken cancellationToken)
    {
        foreach (var (id, watch) in _watches.ToList())
        {
            if (projects.TryGetValue(id, out var root) && string.Equals(root, watch.Root, StringComparison.OrdinalIgnoreCase)) continue;
            watch.Dispose();
            _watches.Remove(id);
        }

        foreach (var (id, root) in projects)
        {
            if (_watches.ContainsKey(id) || !Directory.Exists(root) || !GitTools.IsRepository(root)) continue;
            var folders = await GitFoldersAsync(root, cancellationToken);
            if (folders.Count == 0) continue;

            var project = id;
            var debounce = new Timer(_ => tracker.ProjectChanged(project));
            var watchers = new List<FileSystemWatcher>();
            ProjectWatch? watch = null;
            try
            {
                foreach (var folder in folders)
                {
                    var watcher = new FileSystemWatcher(folder)
                    {
                        IncludeSubdirectories = true,
                        NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite,
                        InternalBufferSize = 64 * 1024
                    };
                    void OnChange(object _, FileSystemEventArgs e)
                    {
                        if (Relevant(e.Name)) watch?.Touched();
                    }
                    watcher.Changed += OnChange;
                    watcher.Created += OnChange;
                    watcher.Deleted += OnChange;
                    watcher.Renamed += (s, e) => OnChange(s, e);
                    // A lost watch (folder gone, buffer overflow) is set up again at the next tick.
                    watcher.Error += (_, _) => watch?.Touched();
                    watchers.Add(watcher);
                }
                watch = new ProjectWatch(root, watchers, debounce);
                foreach (var watcher in watchers) watcher.EnableRaisingEvents = true;
                _watches[id] = watch;
            }
            catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
            {
                logger.LogDebug(ex, "The git folder of {Root} cannot be watched", root);
                foreach (var watcher in watchers) watcher.Dispose();
                await debounce.DisposeAsync();
            }
        }
    }

    // The repository's git folder and, for a worktree, the common one that holds refs and packed-refs.
    private static async Task<IReadOnlyList<string>> GitFoldersAsync(string root, CancellationToken cancellationToken)
    {
        var result = await GitProcess.RunAsync(root, ["rev-parse", "--absolute-git-dir", "--git-common-dir"], cancellationToken);
        if (!result.Ok) return [];
        return GitProcess.Lines(result.Output)
            .Select(line => Path.GetFullPath(Path.IsPathRooted(line) ? line : Path.Combine(root, line)))
            .Where(Directory.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    // Name: relative to the watched .git folder.
    private static bool Relevant(string? name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        var path = name.Replace('\\', '/');
        if (path.EndsWith(".lock", StringComparison.Ordinal)) return false;
        return WatchedFiles.Contains(path, StringComparer.Ordinal)
               || path.StartsWith("refs/", StringComparison.Ordinal)
               || path == "logs/HEAD"
               // A worktree's own HEAD and index sit under worktrees/<name>/.
               || (path.StartsWith("worktrees/", StringComparison.Ordinal) && WatchedFiles.Contains(Path.GetFileName(path), StringComparer.Ordinal));
    }
}

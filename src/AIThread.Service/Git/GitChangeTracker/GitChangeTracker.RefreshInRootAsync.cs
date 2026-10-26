using AIThread.Common.DTOs;
using AIThread.Common.Enums;
using AIThread.Service.Workspace;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Git;

public sealed partial class GitChangeTracker
{
    // The threads of one project folder: the files' fingerprints first (only while a file still holds what the thread
    // wrote), then git's view of every path at once, then the state per file and per thread. Only rows that changed are
    // written; ThreadGit goes to the UI for each thread whose state or commits changed.
    private async Task<int> RefreshInRootAsync(string root, Guid projectId, IReadOnlyList<Guid> threadIds, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root) || !GitTools.IsRepository(root)) return 0;

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var files = await db.ThreadFileSnapshots.AsNoTracking()
            .Where(s => threadIds.Contains(s.ThreadRunId))
            .Select(s => new TrackedFile(s.Id, s.ThreadRunId, s.FilePath, s.Existed, s.Removed, s.OriginalHash, s.AfterHash, s.AfterBlob, s.GitState, s.GitCommit))
            .ToListAsync(cancellationToken);
        if (files.Count == 0) return 0;
        var threads = await db.Threads.AsNoTracking()
            .Where(t => threadIds.Contains(t.Id))
            .Select(t => new { t.Id, t.CreatedAt, t.GitState, t.GitCommits })
            .ToDictionaryAsync(t => t.Id, cancellationToken);

        var paths = files.Select(f => f.FilePath).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var current = paths.ToDictionary(p => p, p => CurrentHash(root, p), StringComparer.OrdinalIgnoreCase);

        bool Unfingerprinted(TrackedFile f) => f.AfterBlob is null && !f.Removed && f.AfterHash is not null && current[f.FilePath] == f.AfterHash;
        var blobs = await CaptureBlobsAsync(root, files.Where(Unfingerprinted).Select(f => f.FilePath).Distinct(StringComparer.OrdinalIgnoreCase).ToList(), cancellationToken);
        var facts = await ReadFactsAsync(root, paths, cancellationToken);

        var history = new Dictionary<string, string?>();
        var now = DateTime.UtcNow;
        var changed = 0;
        foreach (var thread in files.GroupBy(f => f.ThreadId))
        {
            if (!threads.TryGetValue(thread.Key, out var before)) continue;
            var results = new List<(GitChangeState? State, string? Commit)>();
            foreach (var original in thread)
            {
                var file = Unfingerprinted(original) && blobs.TryGetValue(original.FilePath, out var blob) ? original with { AfterBlob = blob } : original;
                var (state, scope) = StateOf(file, current[file.FilePath], facts.GetValueOrDefault(file.FilePath) ?? PathFacts.None);
                var commit = await CommitOfAsync(root, file, scope, before.CreatedAt, history, cancellationToken);
                if (commit is not null && state == GitChangeState.ModifiedOutside) state = GitChangeState.Committed;
                if (commit is not null && state == GitChangeState.Discarded) state = GitChangeState.OnOtherBranch;
                results.Add((state, commit));

                if (state == original.State && commit == original.Commit && file.AfterBlob == original.AfterBlob) continue;
                await db.ThreadFileSnapshots.Where(s => s.Id == file.Id).ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.AfterBlob, file.AfterBlob)
                    .SetProperty(x => x.GitState, state)
                    .SetProperty(x => x.GitCommit, commit), cancellationToken);
            }

            var (threadState, commits) = Summarize(results);
            var commitText = commits.Count == 0 ? null : Clip(string.Join(',', commits), 1000);
            await db.Threads.Where(t => t.Id == thread.Key).ExecuteUpdateAsync(t => t
                .SetProperty(x => x.GitState, threadState)
                .SetProperty(x => x.GitCommits, commitText)
                .SetProperty(x => x.GitCheckedAt, now), cancellationToken);

            if (threadState == before.GitState && commitText == before.GitCommits) continue;
            changed++;
            eventBus.Publish(AppEvent.Create(AppEventTypes.ThreadGit, new { ThreadId = thread.Key, ProjectId = projectId, State = threadState, Commits = commits }, thread.Key));
        }
        return changed;
    }
}

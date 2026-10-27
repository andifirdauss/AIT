using AIThread.Common.Enums;
using AIThread.Service.Workspace;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.Git;

public sealed partial class GitChangeTracker
{
    // Where the change of each thread is, seen from this project's clone, for the memory written from those threads:
    // - a thread of this clone: its own state (here, on another branch, stashed, gone);
    // - another developer's (ThreadChanges): gone when thrown away or lost there; here when HEAD holds one of its commits
    //   or every blob it wrote (a squash merge); in progress when not committed yet; else on another branch.
    // Threads git knows nothing of are left out (the memory counts as here). Never throws.
    public async Task<IReadOnlyDictionary<Guid, MemoryGitPresence>> PresenceAsync(Guid projectId, IReadOnlyCollection<Guid> threadIds,
        CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<Guid, MemoryGitPresence>();
        if (threadIds.Count == 0) return result;
        try
        {
            string? root;
            await using (var db = await dbFactory.CreateDbContextAsync(cancellationToken))
            {
                root = await db.Projects.AsNoTracking().Where(p => p.Id == projectId).Select(p => p.RootPath).FirstOrDefaultAsync(cancellationToken);
                if (root is null) return result;
                var local = await db.Threads.AsNoTracking()
                    .Where(t => threadIds.Contains(t.Id) && t.GitState != null && t.Project != null && t.Project.RootPath == root)
                    .Select(t => new { t.Id, t.GitState, t.GitBranch, t.GitAuthor })
                    .ToListAsync(cancellationToken);
                foreach (var thread in local)
                    result[thread.Id] = new MemoryGitPresence(PlaceOf(thread.GitState), thread.GitState, thread.GitBranch, thread.GitAuthor);
            }

            var others = threadIds.Where(id => !result.ContainsKey(id)).ToList();
            if (others.Count == 0) return result;
            await using var shared = await longStore.CreateGitAsync(cancellationToken);
            var rows = await shared.ThreadChanges.AsNoTracking().Where(c => others.Contains(c.Id)).ToListAsync(cancellationToken);
            if (rows.Count == 0) return result;

            var repository = Directory.Exists(root) && GitTools.IsRepository(root);
            var head = repository ? Value(await GitProcess.RunAsync(root, ["rev-parse", "--verify", "--quiet", "HEAD"], cancellationToken)) : null;
            HashSet<string>? history = null;
            Dictionary<string, PathFacts>? facts = null;
            foreach (var row in rows)
            {
                if (row.State is GitChangeState.Discarded or GitChangeState.Lost)
                {
                    result[row.Id] = new MemoryGitPresence(MemoryGitPlace.Gone, row.State, row.GitBranch, row.GitAuthor);
                    continue;
                }
                if (head is null) continue;
                if (_presence.TryGetValue((root, head, row.Id), out var known))
                {
                    result[row.Id] = known;
                    continue;
                }

                var files = ThreadChangeFiles.Read(row.Files);
                var commits = ThreadChangeFiles.Commits(row.Commits);
                if (commits.Count > 0)
                    history ??= await HeadHistoryAsync(root, rows.Min(r => r.CreatedAt), cancellationToken);
                var place = commits.Any(c => history?.Contains(c) == true) ? MemoryGitPlace.Here : MemoryGitPlace.OtherBranch;
                if (place != MemoryGitPlace.Here && files.Count > 0 && files.All(f => f.Removed || f.Blob is not null))
                {
                    facts ??= await ReadFactsAsync(root, rows.SelectMany(r => ThreadChangeFiles.Read(r.Files)).Select(f => f.Path).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                        cancellationToken);
                    if (files.All(f => facts.GetValueOrDefault(f.Path) is { } fact && (f.Removed ? fact.HeadBlob is null : fact.HeadBlob == f.Blob)))
                        place = MemoryGitPlace.Here;
                }
                if (place != MemoryGitPlace.Here && row.State is GitChangeState.Uncommitted or GitChangeState.Staged or GitChangeState.ModifiedOutside
                        or GitChangeState.Stashed)
                    place = MemoryGitPlace.InProgress;

                var presence = new MemoryGitPresence(place, row.State, row.GitBranch, row.GitAuthor);
                if (_presence.Count >= MaxPresenceCache) _presence.Clear();
                _presence[(root, head, row.Id)] = presence;
                result[row.Id] = presence;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Reading where the memory's changes are failed for project {ProjectId}", projectId);
        }
        return result;
    }
}

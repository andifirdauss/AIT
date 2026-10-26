using AIThread.Common.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.Git;

public sealed partial class GitChangeTracker
{
    // Takes the fingerprint of files that have none yet and works out where each thread's change is now. Threads undone
    // through AIT keep their state. Returns how many threads changed state.
    public async Task<int> RefreshThreadsAsync(IReadOnlyCollection<Guid> threadIds, CancellationToken cancellationToken = default)
    {
        if (threadIds.Count == 0) return 0;
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var threads = await db.Threads.AsNoTracking()
            .Where(t => threadIds.Contains(t.Id) && t.ProjectId != null && t.Status != ThreadStatus.RolledBack && t.RolledBackAt == null)
            .Select(t => new { t.Id, t.ProjectId, Root = t.Project!.RootPath })
            .ToListAsync(cancellationToken);

        var changed = 0;
        foreach (var root in threads.GroupBy(t => t.Root, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                changed += await RefreshInRootAsync(root.Key, root.First().ProjectId!.Value, root.Select(t => t.Id).ToList(), cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Following the git state of the threads in {Root} failed", root.Key);
            }
        }
        return changed;
    }
}

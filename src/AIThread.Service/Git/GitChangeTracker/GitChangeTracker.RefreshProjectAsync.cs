using AIThread.Common.Enums;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Git;

public sealed partial class GitChangeTracker
{
    // The project's threads that are followed: looked at by the tracker before (a thread from before this feature is
    // left alone) and finished within TrackedFor, newest first.
    public async Task<int> RefreshProjectAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        var since = DateTime.UtcNow - TrackedFor;
        List<Guid> ids;
        await using (var db = await dbFactory.CreateDbContextAsync(cancellationToken))
        {
            ids = await db.Threads.AsNoTracking()
                .Where(t => t.ProjectId == projectId && t.GitCheckedAt != null && t.Status != ThreadStatus.RolledBack && t.RolledBackAt == null
                            && (t.CompletedAt ?? t.CreatedAt) >= since)
                .OrderByDescending(t => t.CompletedAt ?? t.CreatedAt)
                .Select(t => t.Id)
                .Take(MaxThreadsPerRefresh)
                .ToListAsync(cancellationToken);
        }
        return await RefreshThreadsAsync(ids, cancellationToken);
    }
}

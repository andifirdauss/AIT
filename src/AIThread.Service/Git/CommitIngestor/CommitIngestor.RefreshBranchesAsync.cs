using AIThread.Common.Enums;
using AIThread.Data.Memory;
using AIThread.Service.Workspace;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Git;

public sealed partial class CommitIngestor
{
    // The working-branch commits this AIT took on in the last FollowedFor, followed in this clone (the only one that is
    // sure to have them): pushed, still committed, or dropped (Lost: the planner of every clone leaves their memory out),
    // and back again when a branch holds them once more. Returns how many changed state.
    private static async Task<int> RefreshBranchesAsync(IGitTrackingDb db, string key, string root, CancellationToken cancellationToken)
    {
        var claimant = Claimant();
        var after = DateTime.UtcNow - FollowedFor;
        var ids = await db.IngestedCommits.AsNoTracking()
            .Where(c => c.ProjectKey == key && c.Branch != null && c.Status == IngestStatus.Done && c.ClaimedBy == claimant && c.CreatedAt > after)
            .Select(c => c.Id).ToListAsync(cancellationToken);
        if (ids.Count == 0) return 0;

        var moved = 0;
        var changes = await db.ThreadChanges.Where(c => ids.Contains(c.Id)).ToListAsync(cancellationToken);
        foreach (var change in changes)
        {
            if (await StateOfAsync(root, ThreadChangeFiles.Commits(change.Commits), cancellationToken) is not { } state || state == change.State) continue;
            change.State = state;
            change.UpdatedBy = GitIdentity.UserName;
            change.UpdatedAt = DateTime.UtcNow;
            moved++;
        }
        if (moved > 0) await db.SaveChangesAsync(cancellationToken);
        return moved;
    }
}

using AIThread.Data.Memory;
using AIThread.Service.Workspace;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Git;

public sealed partial class CommitIngestor
{
    // A copy's commit joins the ThreadChanges of the commits it brings back: git state and environments then find the
    // change through it, also after the branch of the first commit is deleted.
    private static async Task JoinAsync(IGitTrackingDb db, IReadOnlyList<Guid> changeIds, string sha, CancellationToken cancellationToken)
    {
        var changes = await db.ThreadChanges.Where(c => changeIds.Contains(c.Id)).ToListAsync(cancellationToken);
        foreach (var change in changes)
        {
            var commits = ThreadChangeFiles.AddCommit(change.Commits, sha);
            if (commits == change.Commits) continue;
            change.Commits = commits;
            change.UpdatedBy = GitIdentity.UserName;
            change.UpdatedAt = DateTime.UtcNow;
        }
        await db.SaveChangesAsync(cancellationToken);
    }
}

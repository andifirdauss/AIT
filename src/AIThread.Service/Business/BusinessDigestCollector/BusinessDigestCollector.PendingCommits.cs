using AIThread.Common.Entities;
using AIThread.Common.Enums;
using AIThread.Data.Memory;
using AIThread.Service.Git;

namespace AIThread.Service.Business;

public sealed partial class BusinessDigestCollector
{
    // Commits this machine summarized that the wiki has not seen yet (all project keys when key is null).
    private static IQueryable<IngestedCommit> PendingCommits(IGitTrackingDb db, string? key)
    {
        var claimant = CommitIngestor.Claimant();
        return db.IngestedCommits.Where(c => (key == null || c.ProjectKey == key) && c.Status == IngestStatus.Done && c.Digest != null
                                             && c.BusinessDigestedAt == null && c.ClaimedBy == claimant);
    }
}

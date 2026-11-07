using AIThread.Common.Entities;

namespace AIThread.Service.Git;

public sealed partial class CommitIngestor
{
    // A commit summarized in this pass counts for the copies that follow it in the same pass.
    private static void Remember(Summarized summarized, IngestedCommit row, IReadOnlyList<SharedFile> files)
    {
        if (row.PatchId is not null) summarized.Patches.TryAdd(row.PatchId, (row.Id, row.Commit));
        if (row.Branch is not null) summarized.AddBranchCommit(row.Id, row.Branch, files);
    }
}

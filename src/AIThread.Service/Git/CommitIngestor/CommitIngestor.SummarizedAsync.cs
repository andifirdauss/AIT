using AIThread.Common.Enums;
using AIThread.Data.Memory;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Git;

public sealed partial class CommitIngestor
{
    // The commits of the project key summarized before (by any AIT), for CopyOf: their patch-ids, and the path + blob of
    // every file the working-branch ones wrote and the paths they touched (read from their ThreadChange).
    private static async Task<Summarized> SummarizedAsync(IGitTrackingDb db, string key, CancellationToken cancellationToken)
    {
        var summarized = new Summarized();
        var rows = await db.IngestedCommits.AsNoTracking().Where(c => c.ProjectKey == key && c.Status == IngestStatus.Done)
            .Select(c => new { c.Id, c.Commit, c.PatchId, c.Branch }).ToListAsync(cancellationToken);
        foreach (var row in rows.Where(r => r.PatchId is not null)) summarized.Patches.TryAdd(row.PatchId!, (row.Id, row.Commit));

        var branches = rows.Where(r => r.Branch is not null).ToDictionary(r => r.Id, r => r.Branch);
        if (branches.Count == 0) return summarized;
        var ids = branches.Keys.ToList();
        var changes = await db.ThreadChanges.AsNoTracking().Where(c => ids.Contains(c.Id)).Select(c => new { c.Id, c.Files }).ToListAsync(cancellationToken);
        foreach (var change in changes) summarized.AddBranchCommit(change.Id, branches[change.Id]!, ThreadChangeFiles.Read(change.Files));
        return summarized;
    }
}

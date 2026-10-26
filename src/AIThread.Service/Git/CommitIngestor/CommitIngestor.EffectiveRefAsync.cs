using AIThread.Common.Entities;
using AIThread.Data.Memory;
using AIThread.Service.Workspace;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Git;

public sealed partial class CommitIngestor
{
    // The ref whose commits are read: the one set, else the production environment's, else origin/HEAD; null when this
    // clone knows none of them.
    private static async Task<string?> EffectiveRefAsync(IGitTrackingDb db, string key, CommitIngestSettings? settings, string root,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(root) || !GitTools.IsRepository(root)) return null;
        var production = await db.ProjectEnvironments.AsNoTracking().Where(e => e.ProjectKey == key && e.IsProduction)
            .OrderBy(e => e.Order).Select(e => e.Ref).FirstOrDefaultAsync(cancellationToken);
        foreach (var candidate in new[] { settings?.Ref, production, "origin/HEAD" })
        {
            if (string.IsNullOrWhiteSpace(candidate)) continue;
            var result = await GitProcess.RunAsync(root, ["rev-parse", "--verify", "--quiet", "--end-of-options", candidate + "^{commit}"], cancellationToken);
            if (result.Ok) return candidate;
            // A ref that was set but is not here is not silently replaced by another branch.
            if (candidate == settings?.Ref) return null;
        }
        return null;
    }
}

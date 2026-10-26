using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.Business;

public sealed partial class BusinessDigestCollector
{
    // Up to take commits of the key, oldest first, as the wiki writer reads them. An unreachable store only leaves them
    // for the next run.
    private async Task<IReadOnlyList<ThreadDigest>> CommitDigestsAsync(string projectKey, int take, int chars, CancellationToken cancellationToken)
    {
        if (take <= 0) return [];
        try
        {
            await using var db = await longStore.CreateGitAsync(cancellationToken);
            var commits = await PendingCommits(db, projectKey).OrderBy(c => c.CommittedAt).Take(take)
                .Select(c => new { c.Id, c.Digest }).ToListAsync(cancellationToken);
            var digests = new List<ThreadDigest>();
            foreach (var commit in commits)
            {
                if (chars + commit.Digest!.Length > MaxCharsPerBatch && digests.Count > 0) break;
                chars += commit.Digest.Length;
                digests.Add(new ThreadDigest(commit.Id, commit.Digest, []));
            }
            return digests;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "The summarized commits of {ProjectKey} could not be read for the business wiki", projectKey);
            return [];
        }
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.Business;

public sealed partial class BusinessDigestCollector
{
    public async Task<int> CountPendingAsync(string projectKey, CancellationToken cancellationToken)
    {
        int threads;
        await using (var db = await dbFactory.CreateDbContextAsync(cancellationToken))
            threads = await Pending(db, projectKey).CountAsync(cancellationToken);
        try
        {
            await using var git = await longStore.CreateGitAsync(cancellationToken);
            return threads + await PendingCommits(git, projectKey).CountAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "The summarized commits of {ProjectKey} could not be counted", projectKey);
            return threads;
        }
    }
}

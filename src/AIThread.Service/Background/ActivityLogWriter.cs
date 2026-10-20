using AIThread.Common.Entities;
using AIThread.Common.Options;
using AIThread.Data.Context;
using AIThread.Service.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AIThread.Service.Background;

// Persists dashboard log lines in batches and prunes old ones once a day.
public sealed class ActivityLogWriter(
    IActivityLogQueue queue,
    IDbContextFactory<AIThreadDbContext> dbFactory,
    IOptions<AIThreadOptions> options,
    ILogger<ActivityLogWriter> logger) : BackgroundService
{
    private const int BatchSize = 200;
    private DateTime _lastCleanup = DateTime.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var batch = new List<ActivityLog>(BatchSize);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!await queue.Reader.WaitToReadAsync(stoppingToken)) break;
                while (batch.Count < BatchSize && queue.Reader.TryRead(out var entry)) batch.Add(entry);

                await using (var db = await dbFactory.CreateDbContextAsync(stoppingToken))
                {
                    db.ActivityLogs.AddRange(batch);
                    await db.SaveChangesAsync(stoppingToken);
                }
                batch.Clear();

                await CleanupIfDueAsync(stoppingToken);
                await Task.Delay(500, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Database down: drop this batch rather than grow memory without bound.
                logger.LogWarning(ex, "Could not persist {Count} activity log entries.", batch.Count);
                batch.Clear();
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            }
        }
    }

    private async Task CleanupIfDueAsync(CancellationToken cancellationToken)
    {
        if (DateTime.UtcNow - _lastCleanup < TimeSpan.FromHours(24)) return;
        _lastCleanup = DateTime.UtcNow;

        var cutoff = DateTime.UtcNow.AddDays(-Math.Max(1, options.Value.ActivityLogRetentionDays));
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var removed = await db.ActivityLogs.Where(l => l.CreatedAt < cutoff).ExecuteDeleteAsync(cancellationToken);
        if (removed > 0) logger.LogInformation("Pruned {Count} activity log entries older than {Cutoff}.", removed, cutoff);
    }
}

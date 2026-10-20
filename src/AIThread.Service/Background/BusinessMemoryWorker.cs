using AIThread.Common.Options;
using AIThread.Service.Interfaces;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AIThread.Service.Background;

// Summarizes the finished threads into the business wiki every BusinessMemoryIntervalHours (Setting → Business memory).
// Checks the setting every few minutes, so a changed interval or switch applies without a restart.
public sealed class BusinessMemoryWorker(
    IBusinessDigestRunner runner,
    IBusinessDigestCollector collector,
    ISettingService settingService,
    IOptions<AIThreadOptions> options,
    ILogger<BusinessMemoryWorker> logger) : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Let the host finish starting (and the first threads of the day arrive) before the first run.
        await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken).ConfigureAwait(false);
        var lastRun = DateTime.MinValue;
        using var timer = new PeriodicTimer(CheckInterval);

        do
        {
            try
            {
                var settings = await settingService.GetRuntimeAsync(stoppingToken);
                if (settings.BusinessMemoryEnabled && DateTime.UtcNow - lastRun >= TimeSpan.FromHours(Math.Max(1, settings.BusinessMemoryIntervalHours)))
                {
                    lastRun = DateTime.UtcNow;
                    await runner.RunAllAsync(stoppingToken);
                }

                await collector.PruneAttachmentsAsync(TimeSpan.FromDays(Math.Max(1, options.Value.ActivityLogRetentionDays)), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Business memory update failed.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }
}

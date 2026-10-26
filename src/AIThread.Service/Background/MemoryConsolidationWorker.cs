using AIThread.Common.Options;
using AIThread.Service.Interfaces;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AIThread.Service.Background;

// Dumps short memories (Redis) older than the short-term window into long memory (database), then folds the change
// entries that reached long memory (and the facts of older versions) into the summary of their module.
public sealed class MemoryConsolidationWorker(
    IMemoryService memoryService,
    IModuleSummaryWriter summaryWriter,
    IOptions<AIThreadOptions> options,
    ILogger<MemoryConsolidationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Let the host finish starting (and migrations run) before the first pass.
        await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken).ConfigureAwait(false);
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(Math.Max(1, options.Value.MemoryConsolidationIntervalMinutes)));

        do
        {
            try
            {
                await memoryService.ConsolidateAsync(stoppingToken);
                await summaryWriter.RunAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Memory consolidation failed.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }
}

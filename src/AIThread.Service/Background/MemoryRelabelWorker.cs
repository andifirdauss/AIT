using AIThread.Common.Options;
using AIThread.Service.Interfaces;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AIThread.Service.Background;

// Runs MemoryRelabeler on start and every MemoryRelabelIntervalMinutes: memory written by older versions (also by other
// developers' older AIT sharing a store) gets the structured label. A newly created module wakes it early (after a
// short pause, so a burst of new modules is checked once) for the duplicate module check only.
public sealed class MemoryRelabelWorker(
    IMemoryRelabeler relabeler,
    IModuleRegistry registry,
    IOptions<AIThreadOptions> options,
    ILogger<MemoryRelabelWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Debounce = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Let the host finish starting (migrations, 9router, project key backfill) before the first pass.
        await Task.Delay(TimeSpan.FromSeconds(45), stoppingToken).ConfigureAwait(false);
        var interval = TimeSpan.FromMinutes(Math.Max(1, options.Value.MemoryRelabelIntervalMinutes));
        var modulesOnly = false;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await relabeler.RunAsync(modulesOnly, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Memory relabeling failed.");
            }

            try
            {
                var started = DateTime.UtcNow;
                modulesOnly = await registry.WaitForCreatedAsync(interval, stoppingToken);
                if (modulesOnly)
                {
                    await Task.Delay(Debounce, stoppingToken).ConfigureAwait(false);
                    // A full pass is due anyway once the interval has passed.
                    if (DateTime.UtcNow - started >= interval) modulesOnly = false;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}

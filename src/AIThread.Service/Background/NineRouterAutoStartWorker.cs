using AIThread.Common.Options;
using AIThread.Service.Interfaces;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AIThread.Service.Background;

// Starts 9router once when AIThread starts (AIThread:NineRouter:AutoStart), and stops it again on shutdown when
// AIThread started it and StopOnExit is on. 9router going offline later is only reported by RouterMonitorWorker:
// the user may have quit it on purpose.
public sealed class NineRouterAutoStartWorker(
    INineRouterLauncher launcher,
    IOptions<AIThreadOptions> options,
    ILogger<NineRouterAutoStartWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.NineRouter.AutoStart) return;
        await Task.Yield();
        try
        {
            await launcher.EnsureRunningAsync(manual: false, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Starting 9router failed.");
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        if (options.Value.NineRouter.StopOnExit) launcher.StopStarted();
    }
}

using AiThread.Common.DTOs;
using AiThread.Common.Options;
using AiThread.Data.Messaging;
using AiThread.Service.Interfaces;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AiThread.Service.Background;

// Polls 9router and pushes its status to the dashboard; logs only when the state flips.
public sealed class RouterMonitorWorker(
    INineRouterService router,
    IEventBus eventBus,
    IActivityLogService activityLog,
    IOptions<AiThreadOptions> options,
    ILogger<RouterMonitorWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        bool? wasOnline = null;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(5, options.Value.RouterMonitorIntervalSeconds)));

        do
        {
            try
            {
                var status = await router.CheckStatusAsync(stoppingToken);
                eventBus.Publish(AppEvent.Create(AppEventTypes.RouterStatus, status));

                if (wasOnline != status.IsOnline)
                {
                    if (status.IsOnline)
                        activityLog.Success("9router", $"9router terhubung ({status.BaseUrl}, {status.ModelCount} model, {status.ComboCount} combo).");
                    else
                        activityLog.Error("9router", $"9router tidak dapat dihubungi ({status.BaseUrl}): {status.Error}");
                    wasOnline = status.IsOnline;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "9router monitor tick failed.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }
}

using AIThread.Common.DTOs;
using AIThread.Common.Localization;
using AIThread.Common.Options;
using AIThread.Data.Messaging;
using AIThread.Service.CliBackend;
using AIThread.Service.Interfaces;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AIThread.Service.Background;

// Polls 9router and pushes its status to the dashboard; logs only when the state flips.
public sealed class RouterMonitorWorker(
    INineRouterService router,
    INodeService nodeService,
    IEventBus eventBus,
    IActivityLogService activityLog,
    IOptions<AIThreadOptions> options,
    ILogger<RouterMonitorWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        bool? wasOnline = null;
        var keyProblemLogged = false;
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
                        activityLog.Success("9router", Lang.T($"9router terhubung ({status.BaseUrl}, {status.ModelCount} model, {status.ComboCount} combo).", $"9router connected ({status.BaseUrl}, {status.ModelCount} models, {status.ComboCount} combos)."));
                    else if (await CliOnlyAsync(stoppingToken))
                        activityLog.Info("9router", Lang.T($"9router tidak aktif ({status.BaseUrl}); tidak diperlukan karena semua node memakai CLI resmi.", $"9router is not running ({status.BaseUrl}); not needed, since every node uses an official CLI."));
                    else
                        activityLog.Error("9router", Lang.T($"9router tidak dapat dihubungi ({status.BaseUrl}): {status.Error}", $"9router cannot be reached ({status.BaseUrl}): {status.Error}"));
                    wasOnline = status.IsOnline;
                }

                // Online is not enough: chat calls fail if the key is missing or rejected.
                var keyProblem = status.IsOnline && status.ApiKeyValid == false;
                if (keyProblem && !keyProblemLogged) activityLog.Warn("9router", status.Error ?? Lang.T("Masalah API key 9router.", "9router API key problem."));
                else if (!keyProblem && keyProblemLogged && status.IsOnline) activityLog.Success("9router", Lang.T("API key 9router diterima.", "9router API key accepted."));
                keyProblemLogged = keyProblem || (keyProblemLogged && !status.IsOnline);
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

    // Every active node runs on an official CLI (CliBackend), so 9router being offline is expected.
    private async Task<bool> CliOnlyAsync(CancellationToken cancellationToken)
    {
        try
        {
            var active = (await nodeService.GetAllAsync(cancellationToken)).Where(n => n.IsActive).ToList();
            return active.Count > 0 && active.All(n => CliModelId.IsCli(n.Model));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "Could not read the nodes for the 9router monitor.");
            return false;
        }
    }
}

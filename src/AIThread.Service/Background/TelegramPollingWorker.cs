using AIThread.Common.Enums;
using AIThread.Common.Localization;
using AIThread.Common.Options;
using AIThread.Service.Interfaces;
using AIThread.Service.Models;
using AIThread.Service.Telegram;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AIThread.Service.Background;

// Long-polls the Telegram Bot API (getUpdates) so prompts can be sent from a phone while AIThread runs on a laptop
// behind NAT: only outgoing HTTPS to Telegram is needed, no public URL or open port. Starts over whenever the
// Telegram settings change.
public sealed class TelegramPollingWorker(
    ISettingService settingService,
    ITelegramApi api,
    ITelegramBot bot,
    ITelegramBotRuntime runtime,
    IActivityLogService activityLog,
    IOptions<AIThreadOptions> options,
    ILogger<TelegramPollingWorker> logger) : BackgroundService
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(30);

    // Kept across restarts of the loop so a settings change never replays updates.
    private long _botId;
    private long _offset;
    private string? _lastLoggedError;

    // Recently handled update ids: a server that missed the offset would otherwise replay a prompt.
    private readonly HashSet<long> _handled = [];
    private readonly Queue<long> _handledOrder = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Let the host finish starting (and migrations run) first.
        await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken).ConfigureAwait(false);

        while (!stoppingToken.IsCancellationRequested)
        {
            using var session = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, runtime.ReloadToken);
            try
            {
                await RunSessionAsync(session.Token, stoppingToken);
            }
            catch (OperationCanceledException) when (session.IsCancellationRequested)
            {
                // Settings changed, or AIThread is stopping.
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Telegram polling failed; retrying.");
                runtime.Report(TelegramBotStatus.Error, runtime.Snapshot.BotUsername, ex.Message);
                await DelayAsync(RetryDelay, session.Token);
            }
        }
    }

    private async Task RunSessionAsync(CancellationToken token, CancellationToken stoppingToken)
    {
        var settings = await settingService.GetRuntimeAsync(token);
        if (!settings.TelegramEnabled || string.IsNullOrEmpty(settings.TelegramBotToken))
        {
            runtime.Report(TelegramBotStatus.Disabled);
            _lastLoggedError = null;
            await Task.Delay(Timeout.Infinite, token);
            return;
        }

        runtime.Report(TelegramBotStatus.Starting);
        TgUser me;
        try
        {
            me = await api.GetMeAsync(settings.TelegramBotToken, token);
            await api.DeleteWebhookAsync(token);
        }
        catch (TelegramApiException ex)
        {
            await ReportFailureAsync(null, ex, token);
            return;
        }

        try
        {
            await api.SetCommandsAsync(TelegramMessages.Commands, token);
        }
        catch (TelegramApiException ex)
        {
            logger.LogDebug(ex, "setMyCommands failed; the bot's command menu stays as it was.");
        }

        if (me.Id != _botId)
        {
            (_botId, _offset) = (me.Id, 0);
            _handled.Clear();
            _handledOrder.Clear();
        }
        runtime.Report(TelegramBotStatus.Running, me.Username);
        activityLog.Success("telegram", Lang.T($"Bot Telegram @{me.Username} aktif — menunggu pesan.", $"Telegram bot @{me.Username} on — waiting for messages."));
        _lastLoggedError = null;

        var startedAt = DateTime.UtcNow;
        var pollTimeout = Math.Clamp(options.Value.Telegram.PollTimeoutSeconds, 1, 50);
        var failures = 0;
        while (true)
        {
            IReadOnlyList<TgUpdate> updates;
            try
            {
                updates = await api.GetUpdatesAsync(_offset, pollTimeout, token);
            }
            catch (TelegramApiException ex) when (!token.IsCancellationRequested)
            {
                if (ex.IsUnauthorized)
                {
                    await ReportFailureAsync(me.Username, ex, token);
                    return;
                }

                // One dropped poll is normal on a flaky connection; only a persisting problem is reported.
                failures++;
                if (failures >= 2 || ex.IsConflict)
                {
                    var message = ex.IsConflict
                        ? Lang.T("Token ini sedang dipakai program lain untuk menerima pesan. Pastikan hanya satu AIT yang memakai bot ini.", "This token is in use by another program to receive messages. Make sure only one AIT uses this bot.")
                        : ex.Message;
                    runtime.Report(TelegramBotStatus.Error, me.Username, message);
                    LogOnce(message);
                }
                await Task.Delay(ex.IsConflict ? RetryDelay : Backoff(failures), token);
                continue;
            }

            runtime.Polled();
            if (failures > 0)
            {
                failures = 0;
                if (_lastLoggedError is not null)
                {
                    runtime.Report(TelegramBotStatus.Running, me.Username);
                    activityLog.Success("telegram", Lang.T("Koneksi ke Telegram pulih.", "The connection to Telegram recovered."));
                    _lastLoggedError = null;
                }
            }

            foreach (var update in updates)
            {
                // The next poll confirms everything up to here (Telegram: recalculate the offset after each response).
                _offset = update.UpdateId + 1;
                if (!MarkHandled(update.UpdateId)) continue;
                try
                {
                    // The stopping token, not the session token: a settings change must not abort a message halfway.
                    await bot.HandleAsync(update, startedAt, stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
                {
                    logger.LogError(ex, "Telegram update {UpdateId} could not be handled.", update.UpdateId);
                }
            }
        }
    }

    // A rejected token only changes on the Setting page, so wait for that; anything else is retried.
    private async Task ReportFailureAsync(string? botUsername, TelegramApiException ex, CancellationToken token)
    {
        var message = ex.IsUnauthorized ? Lang.T("Token bot ditolak Telegram. Periksa atau ganti token di menu Pengaturan.", "Telegram rejected the bot token. Check or replace the token in the Setting menu.") : ex.Message;
        runtime.Report(TelegramBotStatus.Error, botUsername, message);
        LogOnce(message);
        await Task.Delay(ex.IsUnauthorized ? Timeout.InfiniteTimeSpan : RetryDelay, token);
    }

    // An outage would otherwise add the same line to the dashboard log every retry.
    private void LogOnce(string message)
    {
        if (message == _lastLoggedError) return;
        _lastLoggedError = message;
        activityLog.Error("telegram", Lang.T($"Bot Telegram bermasalah: {message}", $"Telegram bot problem: {message}"));
    }

    private bool MarkHandled(long updateId)
    {
        if (!_handled.Add(updateId)) return false;
        _handledOrder.Enqueue(updateId);
        if (_handledOrder.Count > 500) _handled.Remove(_handledOrder.Dequeue());
        return true;
    }

    private static TimeSpan Backoff(int failures) => TimeSpan.FromSeconds(Math.Min(60, 2 << Math.Min(failures, 5)));

    private static async Task DelayAsync(TimeSpan delay, CancellationToken token)
    {
        try
        {
            await Task.Delay(delay, token);
        }
        catch (OperationCanceledException)
        {
        }
    }
}

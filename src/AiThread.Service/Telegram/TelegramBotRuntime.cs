using AiThread.Common.DTOs;
using AiThread.Common.Enums;
using AiThread.Data.Messaging;

namespace AiThread.Service.Telegram;

public sealed record TelegramBotSnapshot(TelegramBotStatus Status, string? BotUsername, string? Error, DateTime? LastPollAt);

// Live state of the Telegram poller (shown on the Setting page) and the switch that restarts it when the Telegram
// settings change, so a new token or the on/off toggle applies without restarting AiThread.
public sealed class TelegramBotRuntime(IEventBus eventBus)
{
    private CancellationTokenSource _reload = new();
    private volatile TelegramBotSnapshot _snapshot = new(TelegramBotStatus.Disabled, null, null, null);

    public TelegramBotSnapshot Snapshot => _snapshot;

    public CancellationToken ReloadToken => Volatile.Read(ref _reload).Token;

    // Interrupts the current long poll; the worker starts over with the saved settings.
    public void Reload() => Interlocked.Exchange(ref _reload, new CancellationTokenSource()).Cancel();

    public void Report(TelegramBotStatus status, string? botUsername = null, string? error = null)
    {
        var previous = _snapshot;
        _snapshot = previous with { Status = status, BotUsername = botUsername, Error = error };
        if (previous.Status != status || previous.BotUsername != botUsername || previous.Error != error)
            eventBus.Publish(AppEvent.Create(AppEventTypes.TelegramChanged, new { action = "status", status }));
    }

    public void Polled() => _snapshot = _snapshot with { LastPollAt = DateTime.UtcNow };
}

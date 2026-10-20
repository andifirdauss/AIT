using AIThread.Common.Enums;
using AIThread.Data.Messaging;
using AIThread.Service.Interfaces;

namespace AIThread.Service.Telegram;

public sealed record TelegramBotSnapshot(TelegramBotStatus Status, string? BotUsername, string? Error, DateTime? LastPollAt);

// Live state of the Telegram poller (shown on the Setting page) and the switch that restarts it when the Telegram
// settings change, so a new token or the on/off toggle applies without restarting AIThread.
public sealed partial class TelegramBotRuntime(IEventBus eventBus) : ITelegramBotRuntime
{
    private CancellationTokenSource _reload = new();
    private volatile TelegramBotSnapshot _snapshot = new(TelegramBotStatus.Disabled, null, null, null);

    public TelegramBotSnapshot Snapshot => _snapshot;

    public CancellationToken ReloadToken => Volatile.Read(ref _reload).Token;
}

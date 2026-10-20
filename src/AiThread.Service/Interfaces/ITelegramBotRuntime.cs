using AiThread.Common.Enums;
using AiThread.Service.Telegram;

namespace AiThread.Service.Interfaces;

public interface ITelegramBotRuntime
{
    TelegramBotSnapshot Snapshot { get; }

    CancellationToken ReloadToken { get; }

    void Reload();

    void Report(TelegramBotStatus status, string? botUsername = null, string? error = null);

    void Polled();
}

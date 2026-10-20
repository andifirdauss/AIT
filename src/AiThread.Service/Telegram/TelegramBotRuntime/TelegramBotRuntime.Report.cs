using AiThread.Common.DTOs;
using AiThread.Common.Enums;

namespace AiThread.Service.Telegram;

public sealed partial class TelegramBotRuntime
{
    public void Report(TelegramBotStatus status, string? botUsername = null, string? error = null)
    {
        var previous = _snapshot;
        _snapshot = previous with { Status = status, BotUsername = botUsername, Error = error };
        if (previous.Status != status || previous.BotUsername != botUsername || previous.Error != error)
            eventBus.Publish(AppEvent.Create(AppEventTypes.TelegramChanged, new { action = "status", status }));
    }
}

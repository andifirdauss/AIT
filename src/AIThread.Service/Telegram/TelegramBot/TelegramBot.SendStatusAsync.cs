using AIThread.Common.Entities;

namespace AIThread.Service.Telegram;

public sealed partial class TelegramBot
{
    private async Task SendStatusAsync(TelegramChat chat, CancellationToken cancellationToken)
    {
        var running = _running.TryGetValue(chat.ChatId, out var runningId)
            ? (await threadService.GetDetailAsync(runningId, cancellationToken))?.Summary
            : null;
        var recent = await threadService.GetRecentAsync(5, null, cancellationToken);
        await api.SendAsync(chat.ChatId,
            TelegramMessages.Status(await ActiveProjectNameAsync(chat, cancellationToken), running, recent, DateTime.UtcNow),
            cancellationToken: cancellationToken);
    }
}

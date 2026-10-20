using AIThread.Common.Entities;

namespace AIThread.Service.Telegram;

public sealed partial class TelegramBot
{
    private async Task ReplyNotAllowedAsync(TelegramChat chat, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        if (_deniedAt.TryGetValue(chat.ChatId, out var last) && now - last < DeniedReplyInterval) return;
        _deniedAt[chat.ChatId] = now;
        await TrySendAsync(chat.ChatId, TelegramMessages.NotAllowed(chat.DisplayName, chat.ChatId), cancellationToken);
    }
}

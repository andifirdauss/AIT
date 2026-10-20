using AIThread.Common.Entities;

namespace AIThread.Service.Telegram;

public sealed partial class TelegramBot
{
    private async Task HandleAllowedMessageAsync(TelegramChat chat, TgMessage message, DateTime pollingStartedAt, CancellationToken cancellationToken)
    {
        var text = message.Text?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            await api.SendAsync(chat.ChatId, TelegramMessages.TextOnly, cancellationToken: cancellationToken);
            return;
        }

        if (text.StartsWith('/'))
        {
            await HandleCommandAsync(chat, text, cancellationToken);
            return;
        }

        var sentAt = DateTimeOffset.FromUnixTimeSeconds(message.Date).UtcDateTime;
        if (sentAt < pollingStartedAt - StaleGrace)
        {
            await api.SendAsync(chat.ChatId, TelegramMessages.Stale(sentAt), message.MessageId, cancellationToken: cancellationToken);
            return;
        }

        await StartThreadAsync(chat, message.MessageId, text, cancellationToken);
    }
}

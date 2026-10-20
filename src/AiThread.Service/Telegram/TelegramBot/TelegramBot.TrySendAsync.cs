using AiThread.Service.Models;
using Microsoft.Extensions.Logging;

namespace AiThread.Service.Telegram;

public sealed partial class TelegramBot
{
    private async Task TrySendAsync(long chatId, string html, CancellationToken cancellationToken)
    {
        try
        {
            await api.SendAsync(chatId, html, cancellationToken: cancellationToken);
        }
        catch (TelegramApiException ex)
        {
            logger.LogDebug(ex, "Could not send a Telegram message to chat {ChatId}.", chatId);
        }
    }
}

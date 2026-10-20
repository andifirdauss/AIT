using AiThread.Common.Entities;
using AiThread.Service.Interfaces;
using AiThread.Service.Models;

namespace AiThread.Service.Implementations;

public sealed partial class TelegramService
{
    // Best effort: the change is saved even when the bot cannot reach the chat (bot disabled, offline).
    private async Task NotifyAsync(TelegramChat chat, string html, CancellationToken cancellationToken)
    {
        try
        {
            await api.SendAsync(chat.ChatId, html, cancellationToken: cancellationToken);
        }
        catch (TelegramApiException ex)
        {
            activityLog.Warn("telegram", $"Pesan ke chat Telegram {chat.DisplayName} tidak terkirim: {ex.Message}");
        }
    }
}

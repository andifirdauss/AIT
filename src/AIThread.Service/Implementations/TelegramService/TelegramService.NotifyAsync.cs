using AIThread.Common.Entities;
using AIThread.Common.Localization;
using AIThread.Service.Interfaces;
using AIThread.Service.Models;

namespace AIThread.Service.Implementations;

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
            activityLog.Warn("telegram", Lang.T($"Pesan ke chat Telegram {chat.DisplayName} tidak terkirim: {ex.Message}", $"The message to Telegram chat {chat.DisplayName} was not sent: {ex.Message}"));
        }
    }
}

using AIThread.Common.DTOs;
using AIThread.Service.Interfaces;
using AIThread.Service.Telegram;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Implementations;

public sealed partial class TelegramService
{
    public async Task<bool> DeleteChatAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var chat = await db.TelegramChats.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (chat is null) return false;

        db.TelegramChats.Remove(chat);
        await db.SaveChangesAsync(cancellationToken);

        eventBus.Publish(AppEvent.Create(AppEventTypes.TelegramChanged, new { chat.Id, action = "deleted" }));
        if (chat.IsAllowed)
        {
            activityLog.Info("telegram", $"Akses chat Telegram {chat.DisplayName} (ID {chat.ChatId}) dicabut.");
            await NotifyAsync(chat, TelegramMessages.Revoked, cancellationToken);
        }
        return true;
    }
}

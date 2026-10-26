using AIThread.Common.DTOs;
using AIThread.Common.Localization;
using AIThread.Service.Interfaces;
using AIThread.Service.Telegram;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Implementations;

public sealed partial class TelegramService
{
    public async Task<TelegramChatDto?> AllowChatAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var chat = await db.TelegramChats.Include(c => c.ActiveProject).FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (chat is null) return null;

        if (!chat.IsAllowed)
        {
            chat.IsAllowed = true;
            chat.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);

            eventBus.Publish(AppEvent.Create(AppEventTypes.TelegramChanged, new { chat.Id, action = "allowed" }));
            activityLog.Success("telegram", Lang.T($"Chat Telegram {chat.DisplayName} (ID {chat.ChatId}) diizinkan mengirim prompt.", $"Telegram chat {chat.DisplayName} (ID {chat.ChatId}) is allowed to send prompts."));
            await NotifyAsync(chat, TelegramMessages.Allowed(chat.ActiveProject?.Name), cancellationToken);
        }

        return ToDto(chat);
    }
}

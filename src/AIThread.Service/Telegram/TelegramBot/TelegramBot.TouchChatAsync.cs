using AIThread.Common.DTOs;
using AIThread.Common.Entities;
using AIThread.Common.Localization;
using AIThread.Service.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Telegram;

public sealed partial class TelegramBot
{
    // Finds or registers the chat and refreshes its name; a new chat waits for approval on the Setting page.
    private async Task<TelegramChat> TouchChatAsync(TgChat source, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var chat = await db.TelegramChats.FirstOrDefaultAsync(c => c.ChatId == source.Id, cancellationToken);
        var isNew = chat is null;
        if (chat is null)
        {
            var oldPending = await db.TelegramChats
                .Where(c => !c.IsAllowed)
                .OrderByDescending(c => c.LastMessageAt)
                .Skip(MaxPendingChats - 1)
                .ToListAsync(cancellationToken);
            db.TelegramChats.RemoveRange(oldPending);

            chat = new TelegramChat { ChatId = source.Id };
            db.TelegramChats.Add(chat);
        }
        else
        {
            chat.UpdatedAt = DateTime.UtcNow;
        }

        chat.DisplayName = TelegramFormatter.Truncate(DisplayName(source), 200);
        chat.Username = source.Username is { } username ? TelegramFormatter.Truncate(username, 100) : null;
        chat.LastMessageAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        if (isNew)
        {
            eventBus.Publish(AppEvent.Create(AppEventTypes.TelegramChanged, new { chat.Id, action = "created" }));
            activityLog.Warn("telegram",
                Lang.T($"Chat Telegram baru menunggu izin: {chat.DisplayName}{(chat.Username is null ? "" : $" (@{chat.Username})")}, ID {chat.ChatId}. Izinkan di menu Setting bila ini Anda.", $"New Telegram chat waiting for approval: {chat.DisplayName}{(chat.Username is null ? "" : $" (@{chat.Username})")}, ID {chat.ChatId}. Allow it in the Setting menu if it is you."));
        }
        return chat;
    }
}

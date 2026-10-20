using AiThread.Common.Entities;
using Microsoft.EntityFrameworkCore;

namespace AiThread.Service.Telegram;

public sealed partial class TelegramBot
{
    private async Task UpdateChatAsync(long chatId, Action<TelegramChat> mutate, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var chat = await db.TelegramChats.FirstOrDefaultAsync(c => c.ChatId == chatId, cancellationToken);
        if (chat is null) return;

        mutate(chat);
        chat.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }
}

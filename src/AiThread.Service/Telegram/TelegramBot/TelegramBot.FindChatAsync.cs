using AiThread.Common.Entities;
using Microsoft.EntityFrameworkCore;

namespace AiThread.Service.Telegram;

public sealed partial class TelegramBot
{
    private async Task<TelegramChat?> FindChatAsync(long chatId, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.TelegramChats.AsNoTracking().FirstOrDefaultAsync(c => c.ChatId == chatId, cancellationToken);
    }
}

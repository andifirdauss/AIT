using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Telegram;

public sealed partial class TelegramBot
{
    private async Task<Guid?> LastChatThreadAsync(long chatId, CancellationToken cancellationToken)
    {
        var sessionId = SessionIdFor(chatId);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.Threads.AsNoTracking()
            .Where(t => t.SessionId == sessionId)
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => (Guid?)t.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }
}

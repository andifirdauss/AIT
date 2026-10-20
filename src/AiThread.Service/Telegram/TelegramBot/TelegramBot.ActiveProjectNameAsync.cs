using AiThread.Common.Entities;
using Microsoft.EntityFrameworkCore;

namespace AiThread.Service.Telegram;

public sealed partial class TelegramBot
{
    private async Task<string?> ActiveProjectNameAsync(TelegramChat chat, CancellationToken cancellationToken)
    {
        if (chat.ActiveProjectId is not { } id) return null;
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.Projects.AsNoTracking().Where(p => p.Id == id).Select(p => p.Name).FirstOrDefaultAsync(cancellationToken);
    }
}

using Microsoft.EntityFrameworkCore;

namespace AiThread.Service.Telegram;

public sealed partial class TelegramBot
{
    // Sorted by name so the numbers in /projects stay stable.
    private async Task<List<TelegramProject>> ProjectsAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.Projects.AsNoTracking()
            .OrderBy(p => p.Name).ThenBy(p => p.RootPath)
            .Select(p => new TelegramProject(p.Id, p.Name, p.RootPath))
            .ToListAsync(cancellationToken);
    }
}

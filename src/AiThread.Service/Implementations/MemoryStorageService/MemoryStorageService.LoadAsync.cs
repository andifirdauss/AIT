using AiThread.Common.Entities;
using Microsoft.EntityFrameworkCore;

namespace AiThread.Service.Implementations;

public sealed partial class MemoryStorageService
{
    private async Task<SystemSetting> LoadAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.SystemSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Id == SystemSetting.SingletonId, cancellationToken)
            ?? new SystemSetting { Id = SystemSetting.SingletonId };
    }
}

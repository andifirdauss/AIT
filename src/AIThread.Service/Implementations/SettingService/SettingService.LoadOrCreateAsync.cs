using AIThread.Common.Entities;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Implementations;

public sealed partial class SettingService
{
    private async Task<SystemSetting> LoadOrCreateAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.SystemSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Id == SystemSetting.SingletonId, cancellationToken);
        if (entity is not null) return entity;

        entity = new SystemSetting { Id = SystemSetting.SingletonId };
        db.SystemSettings.Add(entity);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Another request created the row first.
            return await db.SystemSettings.AsNoTracking().FirstAsync(s => s.Id == SystemSetting.SingletonId, cancellationToken);
        }
        return entity;
    }
}

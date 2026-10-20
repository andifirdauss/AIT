using AiThread.Common.Entities;
using AiThread.Data.Context;
using Microsoft.EntityFrameworkCore;

namespace AiThread.Service.Implementations;

public sealed partial class SettingService
{
    private static async Task<SystemSetting> LoadTrackedAsync(AiThreadDbContext db, CancellationToken cancellationToken)
    {
        var entity = await db.SystemSettings.FirstOrDefaultAsync(s => s.Id == SystemSetting.SingletonId, cancellationToken);
        if (entity is not null) return entity;

        entity = new SystemSetting { Id = SystemSetting.SingletonId };
        db.SystemSettings.Add(entity);
        return entity;
    }
}

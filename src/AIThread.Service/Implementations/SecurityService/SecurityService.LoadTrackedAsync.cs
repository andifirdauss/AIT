using AIThread.Common.Entities;
using AIThread.Data.Context;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Implementations;

public sealed partial class SecurityService
{
    // The settings row, created with its defaults on a fresh database.
    private static async Task<SystemSetting> LoadTrackedAsync(AIThreadDbContext db, CancellationToken cancellationToken)
    {
        var entity = await db.SystemSettings.FirstOrDefaultAsync(s => s.Id == SystemSetting.SingletonId, cancellationToken);
        if (entity is not null) return entity;

        entity = new SystemSetting { Id = SystemSetting.SingletonId };
        db.SystemSettings.Add(entity);
        return entity;
    }
}

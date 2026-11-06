using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Databases;

public sealed partial class DatabaseBackupStore
{
    public async Task MarkRestoredAsync(Guid backupId, string restoredBy, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var now = DateTime.UtcNow;
        await db.DatabaseBackups.Where(b => b.Id == backupId)
            .ExecuteUpdateAsync(s => s.SetProperty(b => b.RestoredAt, now).SetProperty(b => b.RestoredBy, restoredBy).SetProperty(b => b.UpdatedAt, now), cancellationToken);
    }
}

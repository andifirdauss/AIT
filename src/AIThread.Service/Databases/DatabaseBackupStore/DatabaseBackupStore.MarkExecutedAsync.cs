using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Databases;

public sealed partial class DatabaseBackupStore
{
    public async Task MarkExecutedAsync(Guid backupId, int affectedRows, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var now = DateTime.UtcNow;
        await db.DatabaseBackups.Where(b => b.Id == backupId)
            .ExecuteUpdateAsync(s => s.SetProperty(b => b.ExecutedAt, now).SetProperty(b => b.AffectedRows, affectedRows).SetProperty(b => b.UpdatedAt, now), cancellationToken);
    }
}

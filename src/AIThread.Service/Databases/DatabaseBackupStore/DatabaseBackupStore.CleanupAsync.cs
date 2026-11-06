using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Databases;

public sealed partial class DatabaseBackupStore
{
    public async Task<int> CleanupAsync(CancellationToken cancellationToken = default)
    {
        var days = options.Value.Databases.BackupRetentionDays;
        if (days <= 0) return 0;
        var cutoff = DateTime.UtcNow.AddDays(-days);

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var old = await db.DatabaseBackups.Where(b => b.CreatedAt < cutoff).ToListAsync(cancellationToken);
        foreach (var backup in old) TryDelete(backup.FilePath);
        db.DatabaseBackups.RemoveRange(old);
        await db.SaveChangesAsync(cancellationToken);
        return old.Count;
    }
}

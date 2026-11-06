using AIThread.Common.Entities;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Databases;

public sealed partial class DatabaseBackupStore
{
    // Opens the file (its header); the rows are checked against the record's checksum as they are read.
    public async Task<(DatabaseBackup Record, BackupSnapshot Snapshot)> LoadAsync(Guid backupId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var record = await db.DatabaseBackups.AsNoTracking().SingleOrDefaultAsync(b => b.Id == backupId, cancellationToken)
                     ?? throw new DatabaseToolException("the backup does not exist");
        if (!File.Exists(record.FilePath)) throw new DatabaseToolException($"the backup file is gone ({record.FilePath})");
        return (record, await BackupSnapshot.OpenAsync(record.FilePath, _protector, record.Rows, record.Checksum, cancellationToken));
    }
}

using AIThread.Common.Entities;
using AIThread.Service.Databases;

namespace AIThread.Service.Interfaces;

// The restore points of change_database: what the statement can change, streamed into a compressed, encrypted (Data
// Protection) file on this machine, plus a DatabaseBackups record. A backup counts only after its whole file was read
// back and matched its checksum; there is no row limit, only the disk reserve.
public interface IDatabaseBackupStore
{
    // Streams scope of table to a new file and verifies it; throws DatabaseToolException (and deletes the file) when it
    // cannot be written completely or read back exactly.
    Task<StoredBackup> CreateAsync(DatabaseTarget database, TableName table, BackupScope scope, string statement, Guid? threadId, CancellationToken cancellationToken = default);

    Task<(DatabaseBackup Record, BackupSnapshot Snapshot)> LoadAsync(Guid backupId, CancellationToken cancellationToken = default);

    Task MarkExecutedAsync(Guid backupId, int affectedRows, CancellationToken cancellationToken = default);

    Task MarkRestoredAsync(Guid backupId, string restoredBy, CancellationToken cancellationToken = default);

    // Deletes backups (file, added-keys file and record) older than ProjectDatabaseOptions.BackupRetentionDays.
    Task<int> CleanupAsync(CancellationToken cancellationToken = default);
}

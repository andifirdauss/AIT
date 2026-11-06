using AIThread.Common.Entities;
using AIThread.Service.Databases;

namespace AIThread.Service.Interfaces;

// The restore points of change_database: the table's rows in a compressed, encrypted (Data Protection) file on this
// machine, plus a DatabaseBackups record. A backup counts only after its file was read back and matched its checksum.
public interface IDatabaseBackupStore
{
    // Writes and verifies the file; throws DatabaseToolException when it cannot be read back exactly.
    Task<DatabaseBackup> SaveAsync(DatabaseTarget database, TableSnapshot snapshot, string statement, Guid? threadId, CancellationToken cancellationToken = default);

    Task<(DatabaseBackup Record, TableSnapshot Snapshot)> LoadAsync(Guid backupId, CancellationToken cancellationToken = default);

    Task MarkExecutedAsync(Guid backupId, int affectedRows, CancellationToken cancellationToken = default);

    Task MarkRestoredAsync(Guid backupId, string restoredBy, CancellationToken cancellationToken = default);

    // Deletes backups (file and record) older than ProjectDatabaseOptions.BackupRetentionDays.
    Task<int> CleanupAsync(CancellationToken cancellationToken = default);
}

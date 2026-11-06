using System.Diagnostics;
using AIThread.Common.Entities;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.Databases;

public sealed partial class DatabaseBackupStore
{
    public async Task<StoredBackup> CreateAsync(DatabaseTarget database, TableName table, BackupScope scope, string statement, Guid? threadId,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var id = Guid.NewGuid();
        var folder = Path.Combine(Folder, database.Id.ToString("N"));
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, $"{DateTime.UtcNow:yyyyMMddHHmmss}-{id:N}.bak");

        // The restore point counts only when the whole file reads back as exactly the rows it was made from. Anything
        // else (disk full, connection lost, a damaged file) deletes it: there is no partial backup.
        BackupSnapshot snapshot;
        try
        {
            snapshot = await queries.SnapshotAsync(database, table, scope, path, _protector, cancellationToken);
            var read = 0L;
            await foreach (var _ in snapshot.ReadRowsAsync(cancellationToken)) read++;
            if (read != snapshot.Rows) throw new DatabaseToolException("the backup file did not read back the same rows");
        }
        catch (Exception ex) when (ex is DatabaseToolException or OperationCanceledException)
        {
            TryDelete(path);
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException)
        {
            TryDelete(path);
            throw new DatabaseToolException($"the backup file could not be written or verified: {ex.Message}", ex);
        }

        var record = new DatabaseBackup
        {
            Id = id,
            ProjectDatabaseId = database.Id,
            ThreadRunId = threadId,
            Table = table.Display,
            Statement = statement,
            Rows = (int)Math.Min(int.MaxValue, snapshot.Rows),
            Bytes = snapshot.Bytes,
            Checksum = snapshot.Checksum,
            FilePath = path
        };
        await using (var db = await dbFactory.CreateDbContextAsync(cancellationToken))
        {
            db.DatabaseBackups.Add(record);
            await db.SaveChangesAsync(cancellationToken);
        }
        logger.LogInformation("Backup {Backup} of {Table} on {Database} ({Scope}): {Rows} rows, {Bytes} bytes, {Seconds:0.0} s.", id, record.Table, database.Name,
            scope.Kind, snapshot.Rows, snapshot.Bytes, stopwatch.Elapsed.TotalSeconds);

        try
        {
            await CleanupAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Cleaning old database backups failed.");
        }
        return new StoredBackup(record, snapshot, stopwatch.Elapsed);
    }
}

using AIThread.Common.Entities;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.Databases;

public sealed partial class DatabaseBackupStore
{
    public async Task<DatabaseBackup> SaveAsync(DatabaseTarget database, TableSnapshot snapshot, string statement, Guid? threadId, CancellationToken cancellationToken = default)
    {
        var id = Guid.NewGuid();
        var folder = Path.Combine(Folder, database.Id.ToString("N"));
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, $"{DateTime.UtcNow:yyyyMMddHHmmss}-{id:N}.bak");

        var bytes = Serialize(snapshot);
        await File.WriteAllBytesAsync(path, bytes, cancellationToken);

        // The restore point counts only when the file on disk gives back exactly the rows it was made from.
        var expected = snapshot.Checksum();
        try
        {
            var read = Deserialize(await File.ReadAllBytesAsync(path, cancellationToken), out var stored);
            if (stored != expected || read.Checksum() != expected || read.Rows.Count != snapshot.Rows.Count)
                throw new DatabaseToolException("the backup file did not read back the same rows");
        }
        catch (Exception ex) when (ex is not DatabaseToolException and not OperationCanceledException)
        {
            TryDelete(path);
            throw new DatabaseToolException($"the backup file could not be verified: {ex.Message}", ex);
        }
        catch (DatabaseToolException)
        {
            TryDelete(path);
            throw;
        }

        var record = new DatabaseBackup
        {
            Id = id,
            ProjectDatabaseId = database.Id,
            ThreadRunId = threadId,
            Table = snapshot.Table.Display,
            Statement = statement,
            Rows = snapshot.Rows.Count,
            Bytes = bytes.LongLength,
            Checksum = expected,
            FilePath = path
        };
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        db.DatabaseBackups.Add(record);
        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Backup {Backup} of {Table} on {Database}: {Rows} rows, {Bytes} bytes.", id, record.Table, database.Name, record.Rows, record.Bytes);

        try
        {
            await CleanupAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Cleaning old database backups failed.");
        }
        return record;
    }
}

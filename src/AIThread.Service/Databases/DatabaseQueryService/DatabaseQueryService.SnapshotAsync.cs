using System.Data.Common;
using Microsoft.AspNetCore.DataProtection;

namespace AIThread.Service.Databases;

public sealed partial class DatabaseQueryService
{
    // Streams what the scope covers into a new backup file at path, in a read-only transaction that is rolled back (the
    // WHERE comes from the model's statement). No row limit: the disk is the only limit (DiskSpace). On failure the
    // half-written file is left for the caller to delete; it never counts as a backup (it has no trailer).
    public async Task<BackupSnapshot> SnapshotAsync(DatabaseTarget database, TableName table, BackupScope scope, string path, IDataProtector protector,
        CancellationToken cancellationToken = default)
    {
        var provider = database.Provider;
        await using var connection = await OpenAsync(database, cancellationToken);
        await SqlDialect.BeforeReadOnlyBeginAsync(connection, provider, cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            await SqlDialect.MakeReadOnlyAsync(connection, transaction, provider, cancellationToken);
            var schema = await ReadSchemaAsync(connection, transaction, database, table, cancellationToken);
            var header = new SnapshotHeader(table, schema.Columns, scope, DateTime.UtcNow);
            if (!header.HasKey)
                throw new DatabaseToolException($"the table '{table.Display}' has no primary key, so AIT cannot restore it row by row; change it by hand (with your own backup) or add a primary key first");

            // An INSERT's backup holds no rows (the keys it adds are recorded when it runs).
            var (from, where) = Source(provider, header);
            var expected = scope.Kind == BackupScopeKind.Insert ? 0 : await CountAsync(connection, transaction, from, where, cancellationToken);
            await using var writer = await SnapshotWriter.CreateAsync(path, protector, header, Reserve, expected, cancellationToken);
            if (scope.Kind != BackupScopeKind.Insert)
            {
                await foreach (var row in ReadRowsAsync(connection, transaction, provider, header.Columns, from, where, [], cancellationToken))
                    await writer.WriteAsync(row, cancellationToken);
            }
            var summary = await writer.CompleteAsync(cancellationToken);
            return new BackupSnapshot(path, protector, header, summary.Rows, summary.Checksum, summary.Bytes);
        }
        catch (DbException ex)
        {
            throw new DatabaseToolException($"the backup of '{table.Display}' could not be read: {ex.Message}", ex);
        }
        catch (IOException ex)
        {
            throw new DatabaseToolException($"the backup file could not be written: {ex.Message}", ex);
        }
        finally
        {
            await RollbackQuietlyAsync(transaction);
            await SqlDialect.AfterReadOnlyAsync(connection, provider, CancellationToken.None);
        }
    }
}

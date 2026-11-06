namespace AIThread.Service.Databases;

public sealed partial class DatabaseQueryService
{
    // Commits only when the table then matches the backup exactly; otherwise everything is rolled back.
    public async Task<RestoreCounts> RestoreAsync(DatabaseTarget database, TableSnapshot backup, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(database, cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var committed = false;
        try
        {
            await EnsureTransactionalAsync(connection, transaction, database, backup.Table, cancellationToken);
            var counts = await ApplyRestoreAsync(connection, transaction, database, backup, cancellationToken);
            var schema = await ReadSchemaAsync(connection, transaction, database, backup.Table, cancellationToken);
            var restored = await ReadSnapshotAsync(connection, transaction, database, schema, cancellationToken);
            if (restored.Checksum() != backup.Checksum())
                throw new DatabaseToolException("after the restore the table did not match the backup, so nothing was committed");
            await transaction.CommitAsync(cancellationToken);
            committed = true;
            return counts;
        }
        finally
        {
            if (!committed) await RollbackQuietlyAsync(transaction);
        }
    }
}

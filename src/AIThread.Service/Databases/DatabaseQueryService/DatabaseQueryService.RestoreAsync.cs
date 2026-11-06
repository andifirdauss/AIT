using System.Data.Common;

namespace AIThread.Service.Databases;

public sealed partial class DatabaseQueryService
{
    // Commits only when the rows the backup covers then match it exactly; otherwise everything is rolled back. A WHERE
    // backup touches only its own rows (what others changed elsewhere in the table stays); an INSERT's backup deletes
    // the rows the INSERT added.
    public async Task<RestoreCounts> RestoreAsync(DatabaseTarget database, BackupSnapshot backup, CancellationToken cancellationToken = default)
    {
        var header = backup.Header;
        await using var connection = await OpenAsync(database, cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var committed = false;
        try
        {
            await EnsureTransactionalAsync(connection, transaction, database, header.Table, cancellationToken);
            await MatchSchemaAsync(connection, transaction, database, header, cancellationToken);

            RestoreCounts counts;
            if (header.Scope.Kind == BackupScopeKind.Insert)
            {
                // Never executed: nothing was added, so nothing to undo.
                if (!backup.HasAddedKeys) return new RestoreCounts(0, 0, 0);
                var keys = header.KeysOnly();
                var deleted = await DeleteKeysAsync(connection, transaction, database, keys, backup.ReadAddedKeysAsync(cancellationToken), cancellationToken);
                await foreach (var _ in LookupAsync(connection, transaction, database.Provider, keys, backup.ReadAddedKeysAsync(cancellationToken), cancellationToken))
                    throw new DatabaseToolException("after the restore some of the added rows were still there, so nothing was committed");
                counts = new RestoreCounts(0, 0, deleted);
            }
            else
            {
                await using var scratch = await OpenScratchAsync(header, cancellationToken);
                await scratch.LoadAsync(DiffScratch.Side.Backup, backup.ReadRowsAsync(cancellationToken), cancellationToken);
                await scratch.LoadAsync(DiffScratch.Side.Current, CurrentRowsAsync(connection, transaction, database, backup, cancellationToken), cancellationToken);
                counts = await ApplyRestoreAsync(connection, transaction, database, header, scratch, cancellationToken);
                var restored = await RowChecksum.OfAsync(header, CurrentRowsAsync(connection, transaction, database, backup, cancellationToken), cancellationToken);
                if (restored.Value != backup.Checksum)
                    throw new DatabaseToolException("after the restore the rows did not match the backup, so nothing was committed");
            }

            await transaction.CommitAsync(cancellationToken);
            committed = true;
            return counts;
        }
        catch (DbException ex)
        {
            throw new DatabaseToolException($"the restore failed and was rolled back: {ex.Message}", ex);
        }
        finally
        {
            if (!committed) await RollbackQuietlyAsync(transaction);
        }
    }
}

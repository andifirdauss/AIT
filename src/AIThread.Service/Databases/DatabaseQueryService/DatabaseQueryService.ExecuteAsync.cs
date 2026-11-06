using System.Data.Common;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.Databases;

public sealed partial class DatabaseQueryService
{
    // In one transaction: the rows the backup covers must still be exactly the backup (otherwise the restore point would
    // not undo what runs now), the statement must affect exactly expectedRows (the dry test the developer approved),
    // and for an INSERT the keys it added are written beside the backup before the commit. Anything else rolls back.
    public async Task<int> ExecuteAsync(DatabaseTarget database, string sql, int expectedRows, BackupSnapshot backup, CancellationToken cancellationToken = default)
    {
        var check = SqlStatement.Classify(sql, database.Provider);
        if (check.Kind != SqlStatementKind.Write || check.Target is null) throw new DatabaseToolException(check.Reason ?? "only INSERT, UPDATE, DELETE and MERGE can change rows");
        var header = backup.Header;
        if (!string.Equals(check.Target.Name, header.Table.Name, StringComparison.OrdinalIgnoreCase))
            throw new DatabaseToolException($"the backup is of '{header.Table.Display}', but the statement changes '{check.Target.Display}'");

        var provider = database.Provider;
        await using var connection = await OpenAsync(database, cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var committed = false;
        var keysWritten = false;
        DiffScratch? scratch = null;
        try
        {
            await EnsureTransactionalAsync(connection, transaction, database, header.Table, cancellationToken);
            await MatchSchemaAsync(connection, transaction, database, header, cancellationToken);
            var table = SqlDialect.QuoteTable(provider, header.Table);
            var keys = header.KeysOnly();

            if (header.Scope.Kind == BackupScopeKind.Insert)
            {
                scratch = await OpenScratchAsync(keys, cancellationToken);
                await scratch.LoadAsync(DiffScratch.Side.Backup, ReadRowsAsync(connection, transaction, provider, keys.Columns, table, null, [], cancellationToken), cancellationToken);
            }
            else
            {
                var (from, where) = Source(provider, header);
                var now = await RowChecksum.OfAsync(header, ReadRowsAsync(connection, transaction, provider, header.Columns, from, where, [], cancellationToken), cancellationToken);
                if (now.Value != backup.Checksum)
                    throw new DatabaseToolException("the rows the backup covers changed since it was made (someone else wrote to the table), so the backup would not undo this change; nothing was run. Call change_database again for a fresh backup and dry test");
            }

            int affected;
            try
            {
                await using var command = Command(connection, transaction, sql);
                affected = await command.ExecuteNonQueryAsync(cancellationToken);
            }
            catch (DbException ex)
            {
                throw new DatabaseToolException($"the change failed and was rolled back: {ex.Message}", ex);
            }

            // The data moved since the dry test (someone else wrote to the table): what was approved is not what would run.
            if (affected != expectedRows)
                throw new DatabaseToolException($"the change would affect {affected} rows, not the {expectedRows} of the dry test the developer approved; it was rolled back");

            if (scratch is not null)
            {
                await scratch.LoadAsync(DiffScratch.Side.Current, ReadRowsAsync(connection, transaction, provider, keys.Columns, table, null, [], cancellationToken), cancellationToken);
                // Keys that someone else inserted meanwhile must not end up in the list a restore deletes.
                var added = await scratch.CountOnlyInAsync(DiffScratch.Side.Current, cancellationToken);
                if (added != affected || await scratch.CountOnlyInAsync(DiffScratch.Side.Backup, cancellationToken) > 0)
                    throw new DatabaseToolException($"{added} new keys appeared for the {affected} inserted rows (someone else wrote to the table at the same time), so the rows to undo could not be told apart; it was rolled back, try again");
                await backup.WriteAddedKeysAsync(scratch.OnlyInAsync(DiffScratch.Side.Current, cancellationToken), Reserve, cancellationToken);
                keysWritten = true;
            }

            await transaction.CommitAsync(cancellationToken);
            committed = true;
            logger.LogInformation("Change on project database {Database} committed: {Rows} rows.", database.Name, affected);
            return affected;
        }
        finally
        {
            if (scratch is not null) await scratch.DisposeAsync();
            if (!committed)
            {
                await RollbackQuietlyAsync(transaction);
                if (keysWritten) backup.DeleteAddedKeys();
            }
        }
    }
}

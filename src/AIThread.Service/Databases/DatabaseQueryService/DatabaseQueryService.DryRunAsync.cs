using System.Data.Common;

namespace AIThread.Service.Databases;

public sealed partial class DatabaseQueryService
{
    // One transaction, always rolled back: the change runs (rows affected, what it changed), then the backup is restored
    // and the table compared with it. Problem says why the change may not go ahead; nothing is left changed either way.
    public async Task<DryRunResult> DryRunAsync(DatabaseTarget database, string sql, TableSnapshot backup, CancellationToken cancellationToken = default)
    {
        var check = SqlStatement.Classify(sql, database.Provider);
        if (check.Kind != SqlStatementKind.Write) throw new DatabaseToolException(check.Reason ?? "only INSERT, UPDATE, DELETE and MERGE can change rows");

        await using var connection = await OpenAsync(database, cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            await EnsureTransactionalAsync(connection, transaction, database, backup.Table, cancellationToken);
            var schema = await ReadSchemaAsync(connection, transaction, database, backup.Table, cancellationToken);

            int affected;
            try
            {
                await using var command = Command(connection, transaction, sql);
                affected = await command.ExecuteNonQueryAsync(cancellationToken);
            }
            catch (DbException ex)
            {
                return new DryRunResult(0, [], 0, new RestoreCounts(0, 0, 0), false, $"the change failed in the dry test: {ex.Message}");
            }

            var after = await ReadSnapshotAsync(connection, transaction, database, schema, cancellationToken);
            var (changed, sample) = Diff(backup, after);

            RestoreCounts restore;
            try
            {
                restore = await ApplyRestoreAsync(connection, transaction, database, backup, cancellationToken);
            }
            catch (Exception ex) when (ex is DbException or DatabaseToolException)
            {
                return new DryRunResult(affected, sample, changed, new RestoreCounts(0, 0, 0), false, $"the restore test failed: {ex.Message}");
            }

            var restored = await ReadSnapshotAsync(connection, transaction, database, schema, cancellationToken);
            var verified = restored.Checksum() == backup.Checksum();
            return new DryRunResult(affected, sample, changed, restore, verified,
                verified ? null : "after the restore test the table did not match the backup (a trigger or a default may change rows), so a restore could not be trusted");
        }
        finally
        {
            await RollbackQuietlyAsync(transaction);
        }
    }
}

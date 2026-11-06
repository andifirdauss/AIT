using System.Diagnostics;

namespace AIThread.Service.Databases;

public sealed partial class DatabaseQueryService
{
    // One transaction, always rolled back: the change runs (rows affected, what it changed), then the backup is restored
    // and compared again. Problem says why the change may not go ahead; nothing is left changed either way. Elapsed is
    // about how long the real change will hold its transaction (and its locks).
    public async Task<DryRunResult> DryRunAsync(DatabaseTarget database, string sql, BackupSnapshot backup, CancellationToken cancellationToken = default)
    {
        var check = SqlStatement.Classify(sql, database.Provider);
        if (check.Kind != SqlStatementKind.Write) throw new DatabaseToolException(check.Reason ?? "only INSERT, UPDATE, DELETE and MERGE can change rows");

        var stopwatch = Stopwatch.StartNew();
        await using var connection = await OpenAsync(database, cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            await EnsureTransactionalAsync(connection, transaction, database, backup.Header.Table, cancellationToken);
            await MatchSchemaAsync(connection, transaction, database, backup.Header, cancellationToken);
            var result = backup.Header.Scope.Kind == BackupScopeKind.Insert
                ? await DryRunInsertAsync(connection, transaction, database, sql, backup, cancellationToken)
                : await DryRunRowsAsync(connection, transaction, database, sql, backup, cancellationToken);
            return result with { Elapsed = stopwatch.Elapsed };
        }
        finally
        {
            await RollbackQuietlyAsync(transaction);
        }
    }
}

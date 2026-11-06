using System.Data.Common;
using System.Diagnostics;

namespace AIThread.Service.Databases;

public sealed partial class DatabaseQueryService
{
    public async Task<DbQueryResult> QueryAsync(DatabaseTarget database, string sql, int maxRows, CancellationToken cancellationToken = default)
    {
        var check = SqlStatement.Classify(sql, database.Provider);
        if (check.Kind != SqlStatementKind.Read)
            throw new DatabaseToolException(check.Kind == SqlStatementKind.Write
                ? "query_database only reads; change rows with change_database"
                : $"the statement was refused: {check.Reason}");

        var stopwatch = Stopwatch.StartNew();
        await using var connection = await OpenAsync(database, cancellationToken);
        await SqlDialect.BeforeReadOnlyBeginAsync(connection, database.Provider, cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            await SqlDialect.MakeReadOnlyAsync(connection, transaction, database.Provider, cancellationToken);
            await using var command = Command(connection, transaction, sql);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            var columns = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToList();
            var rows = new List<Cell[]>();
            var truncated = false;
            while (await reader.ReadAsync(cancellationToken))
            {
                if (rows.Count >= maxRows)
                {
                    truncated = true;
                    break;
                }
                var row = new Cell[columns.Count];
                for (var i = 0; i < columns.Count; i++) row[i] = Cell.From(await reader.IsDBNullAsync(i, cancellationToken) ? null : reader.GetValue(i));
                rows.Add(row);
            }
            return new DbQueryResult(columns, rows, truncated, stopwatch.Elapsed);
        }
        catch (DbException ex)
        {
            throw new DatabaseToolException($"the query failed: {ex.Message}", ex);
        }
        finally
        {
            // A read never commits: whatever it may have touched goes away.
            await RollbackQuietlyAsync(transaction);
            await SqlDialect.AfterReadOnlyAsync(connection, database.Provider, CancellationToken.None);
        }
    }
}

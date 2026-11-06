using System.Data.Common;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.Databases;

public sealed partial class DatabaseQueryService
{
    public async Task<int> ExecuteAsync(DatabaseTarget database, string sql, int expectedRows, CancellationToken cancellationToken = default)
    {
        var check = SqlStatement.Classify(sql, database.Provider);
        if (check.Kind != SqlStatementKind.Write || check.Target is null) throw new DatabaseToolException(check.Reason ?? "only INSERT, UPDATE, DELETE and MERGE can change rows");

        await using var connection = await OpenAsync(database, cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var committed = false;
        try
        {
            await EnsureTransactionalAsync(connection, transaction, database, check.Target, cancellationToken);
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

            await transaction.CommitAsync(cancellationToken);
            committed = true;
            logger.LogInformation("Change on project database {Database} committed: {Rows} rows.", database.Name, affected);
            return affected;
        }
        finally
        {
            if (!committed) await RollbackQuietlyAsync(transaction);
        }
    }
}

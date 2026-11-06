using System.Data.Common;

namespace AIThread.Service.Databases;

public sealed partial class DatabaseQueryService
{
    // A dry test is only harmless when the engine can roll it back: a MySQL table that is not InnoDB cannot.
    private async Task EnsureTransactionalAsync(DbConnection connection, DbTransaction transaction, DatabaseTarget database, TableName table,
        CancellationToken cancellationToken)
    {
        var sql = SqlDialect.TransactionalCheck(database.Provider);
        if (sql is null) return;
        await using var command = Command(connection, transaction, sql, table.Name, table.Schema);
        var engine = await command.ExecuteScalarAsync(cancellationToken) as string;
        if (!string.Equals(engine, "InnoDB", StringComparison.OrdinalIgnoreCase))
            throw new DatabaseToolException($"the table '{table.Display}' uses the {engine ?? "unknown"} engine, which has no transactions: a dry test there would really change the data, so AIT does not change it");
    }
}

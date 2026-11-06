using System.Data.Common;
using System.Globalization;

namespace AIThread.Service.Databases;

public sealed partial class DatabaseQueryService
{
    private async Task<long> CountAsync(DbConnection connection, DbTransaction? transaction, DatabaseTarget database, TableName table, CancellationToken cancellationToken)
    {
        try
        {
            await using var command = Command(connection, transaction, $"SELECT COUNT(*) FROM {SqlDialect.QuoteTable(database.Provider, table)}");
            return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
        }
        catch (DbException ex)
        {
            throw new DatabaseToolException($"the rows of '{table.Display}' cannot be counted: {ex.Message}", ex);
        }
    }
}

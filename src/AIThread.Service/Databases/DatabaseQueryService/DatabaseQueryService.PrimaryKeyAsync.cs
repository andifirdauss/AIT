using System.Data.Common;
using AIThread.Common.Enums;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.Databases;

public sealed partial class DatabaseQueryService
{
    private async Task<IReadOnlyList<string>> PrimaryKeyAsync(DbConnection connection, DbTransaction? transaction, MemoryStoreProvider provider, TableName table,
        CancellationToken cancellationToken)
    {
        var sql = SqlDialect.PrimaryKeyQuery(provider, table);
        if (sql is null) return [];
        try
        {
            await using var command = Command(connection, transaction, sql, [.. SqlDialect.PrimaryKeyParameters(provider, table)]);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            var keys = new List<string>();
            while (await reader.ReadAsync(cancellationToken)) keys.Add(reader.GetString(0));
            return keys;
        }
        catch (DbException ex)
        {
            // The catalog may be closed to this user; the driver's key info is the fallback.
            logger.LogDebug(ex, "Primary key lookup of {Table} failed.", table.Display);
            return [];
        }
    }
}

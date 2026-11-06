using System.Data.Common;
using AIThread.Common.Enums;

namespace AIThread.Service.Databases;

public sealed partial class DatabaseQueryService
{
    // One copied table in the rehearsal file: SQLite affinities for the source types, the same primary key, all rows.
    private async Task LoadCopyAsync(DbConnection connection, TableSnapshot copy, CancellationToken cancellationToken)
    {
        const MemoryStoreProvider sqlite = MemoryStoreProvider.Sqlite;
        var columns = string.Join(", ", copy.Columns.Select(c => $"{SqlDialect.Quote(sqlite, c.Name)} {Affinity(c.Type)}"));
        var keys = copy.Columns.Where(c => c.IsKey).Select(c => SqlDialect.Quote(sqlite, c.Name)).ToList();
        var primaryKey = keys.Count > 0 ? $", PRIMARY KEY ({string.Join(", ", keys)})" : string.Empty;
        var table = SqlDialect.Quote(sqlite, copy.Table.Name);

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var create = Command(connection, transaction, $"CREATE TABLE {table} ({columns}{primaryKey})"))
            await create.ExecuteNonQueryAsync(cancellationToken);

        var list = string.Join(", ", copy.Columns.Select(c => SqlDialect.Quote(sqlite, c.Name)));
        var values = string.Join(", ", copy.Columns.Select((_, i) => SqlDialect.Parameter(sqlite, i)));
        foreach (var row in copy.Rows)
        {
            await using var insert = Command(connection, transaction, $"INSERT INTO {table} ({list}) VALUES ({values})", [.. row.Select(SqliteValue)]);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
    }
}

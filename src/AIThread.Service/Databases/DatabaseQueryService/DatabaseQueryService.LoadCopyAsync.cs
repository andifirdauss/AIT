using System.Data.Common;
using AIThread.Common.Enums;

namespace AIThread.Service.Databases;

public sealed partial class DatabaseQueryService
{
    // Rows per transaction of a rehearsal copy; the disk reserve is checked between them.
    private const int CopyBatchRows = 10_000;

    // One table streamed from the target into the rehearsal file: SQLite affinities for the source types, the same
    // primary key, all rows, through one reused INSERT (SQLite compiles it once).
    private async Task LoadCopyAsync(DbConnection source, DatabaseTarget database, TableSchema schema, DbConnection copy, string file, CancellationToken cancellationToken)
    {
        const MemoryStoreProvider sqlite = MemoryStoreProvider.Sqlite;
        var columns = string.Join(", ", schema.Columns.Select(c => $"{SqlDialect.Quote(sqlite, c.Name)} {Affinity(c.Type)}"));
        var keys = schema.Columns.Where(c => c.IsKey).Select(c => SqlDialect.Quote(sqlite, c.Name)).ToList();
        var primaryKey = keys.Count > 0 ? $", PRIMARY KEY ({string.Join(", ", keys)})" : string.Empty;
        var table = SqlDialect.Quote(sqlite, schema.Table.Name);

        DiskSpace.Ensure(file, 0, Reserve, "the rehearsal copy");
        await using (var create = Command(copy, null, $"CREATE TABLE {table} ({columns}{primaryKey})"))
            await create.ExecuteNonQueryAsync(cancellationToken);

        var list = string.Join(", ", schema.Columns.Select(c => SqlDialect.Quote(sqlite, c.Name)));
        var values = string.Join(", ", schema.Columns.Select((_, i) => SqlDialect.Parameter(sqlite, i)));
        var transaction = await copy.BeginTransactionAsync(cancellationToken);
        try
        {
            await using var insert = Command(copy, transaction, $"INSERT INTO {table} ({list}) VALUES ({values})", new object?[schema.Columns.Count]);
            var count = 0L;
            await foreach (var row in ReadRowsAsync(source, null, database.Provider, schema.Columns, SqlDialect.QuoteTable(database.Provider, schema.Table), null, [], cancellationToken))
            {
                for (var i = 0; i < row.Length; i++) insert.Parameters[i].Value = SqliteValue(row[i]) ?? DBNull.Value;
                await insert.ExecuteNonQueryAsync(cancellationToken);
                if (++count % CopyBatchRows != 0) continue;
                await transaction.CommitAsync(cancellationToken);
                await transaction.DisposeAsync();
                DiskSpace.Ensure(file, 0, Reserve, "the rehearsal copy");
                transaction = await copy.BeginTransactionAsync(cancellationToken);
                insert.Transaction = transaction;
            }
            await transaction.CommitAsync(cancellationToken);
        }
        finally
        {
            await transaction.DisposeAsync();
        }
    }
}

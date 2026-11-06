using System.Data.Common;

namespace AIThread.Service.Databases;

public sealed partial class DatabaseQueryService
{
    private async Task<TableSnapshot> ReadSnapshotAsync(DbConnection connection, DbTransaction? transaction, DatabaseTarget database, TableSchema schema,
        CancellationToken cancellationToken)
    {
        var provider = database.Provider;
        var list = string.Join(", ", schema.Columns.Select(c => SqlDialect.Quote(provider, c.Name)));
        try
        {
            await using var command = Command(connection, transaction, $"SELECT {list} FROM {SqlDialect.QuoteTable(provider, schema.Table)}");
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            var rows = await TableSnapshot.ReadRowsAsync(reader, schema.Columns.Count, Math.Max(1, Limits.BackupMaxRows), cancellationToken);
            return new TableSnapshot { Table = schema.Table, Columns = schema.Columns, Rows = rows };
        }
        catch (DbException ex)
        {
            throw new DatabaseToolException($"the rows of '{schema.Table.Display}' cannot be read: {ex.Message}", ex);
        }
    }
}

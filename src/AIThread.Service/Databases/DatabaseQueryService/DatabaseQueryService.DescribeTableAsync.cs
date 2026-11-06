namespace AIThread.Service.Databases;

public sealed partial class DatabaseQueryService
{
    public async Task<TableDescription> DescribeTableAsync(DatabaseTarget database, TableName table, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(database, cancellationToken);
        var schema = await ReadSchemaAsync(connection, null, database, table, cancellationToken);
        var rows = await CountAsync(connection, null, SqlDialect.QuoteTable(database.Provider, table), null, cancellationToken);
        return new TableDescription(table, schema.Columns, rows);
    }
}

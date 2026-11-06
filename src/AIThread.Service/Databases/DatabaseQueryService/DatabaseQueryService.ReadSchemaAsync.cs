using System.Data;
using System.Data.Common;
using AIThread.Common.Enums;

namespace AIThread.Service.Databases;

public sealed partial class DatabaseQueryService
{
    // Columns from an empty SELECT (types, identity, computed), the primary key from the engine's catalog.
    private async Task<TableSchema> ReadSchemaAsync(DbConnection connection, DbTransaction? transaction, DatabaseTarget database, TableName table,
        CancellationToken cancellationToken)
    {
        var provider = database.Provider;
        var keys = await PrimaryKeyAsync(connection, transaction, provider, table, cancellationToken);

        IReadOnlyList<DbColumn> schema;
        try
        {
            await using var command = Command(connection, transaction, $"SELECT * FROM {SqlDialect.QuoteTable(provider, table)} WHERE 1 = 0");
            await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SchemaOnly | CommandBehavior.KeyInfo, cancellationToken);
            schema = reader.GetColumnSchema();
        }
        catch (DbException ex)
        {
            throw new DatabaseToolException($"the table '{table.Display}' cannot be read: {ex.Message}", ex);
        }

        var columns = schema
            .Where(c => c.IsHidden != true)
            .Select(c =>
            {
                var type = c.DataTypeName ?? c.DataType?.Name ?? "?";
                var identity = c.IsIdentity == true || c.IsAutoIncrement == true;
                var rowVersion = provider == MemoryStoreProvider.SqlServer
                                 && (type.Equals("timestamp", StringComparison.OrdinalIgnoreCase) || type.Equals("rowversion", StringComparison.OrdinalIgnoreCase));
                var computed = c.IsExpression == true || rowVersion || (c.IsReadOnly == true && !identity);
                var isKey = keys.Count > 0 ? keys.Contains(c.ColumnName, StringComparer.OrdinalIgnoreCase) : c.IsKey == true;
                return new SnapshotColumn(c.ColumnName, type, isKey, identity, !computed, c.AllowDBNull != false);
            })
            .ToList();
        if (columns.Count == 0) throw new DatabaseToolException($"the table '{table.Display}' has no columns AIT can read");
        return new TableSchema(table, columns);
    }
}

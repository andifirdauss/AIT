namespace AIThread.Service.Databases;

public sealed partial class DatabaseQueryService
{
    public async Task<TableSnapshot> SnapshotAsync(DatabaseTarget database, TableName table, bool forRestore, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(database, cancellationToken);
        var schema = await ReadSchemaAsync(connection, null, database, table, cancellationToken);
        if (forRestore && !schema.Columns.Any(c => c.IsKey))
            throw new DatabaseToolException($"the table '{table.Display}' has no primary key, so AIT cannot restore it row by row; change it by hand (with your own backup) or add a primary key first");

        var count = await CountAsync(connection, null, database, table, cancellationToken);
        if (count > Limits.BackupMaxRows)
            throw new DatabaseToolException($"the table '{table.Display}' has {count} rows, more than the {Limits.BackupMaxRows} AIT backs up; have a DBA back it up and run the change by hand");
        return await ReadSnapshotAsync(connection, null, database, schema, cancellationToken);
    }
}

using System.Data.Common;

namespace AIThread.Service.Databases;

public sealed partial class DatabaseQueryService
{
    // The table still has the columns of the backup, in the same order: otherwise rows cannot be compared or put back.
    private async Task MatchSchemaAsync(DbConnection connection, DbTransaction transaction, DatabaseTarget database, SnapshotHeader header, CancellationToken cancellationToken)
    {
        var schema = await ReadSchemaAsync(connection, transaction, database, header.Table, cancellationToken);
        if (!schema.Columns.Select(c => c.Name).SequenceEqual(header.Columns.Select(c => c.Name), StringComparer.OrdinalIgnoreCase))
            throw new DatabaseToolException($"the columns of '{header.Table.Display}' changed since the backup, so it cannot be compared with it or restored automatically");
    }
}

using System.Data.Common;

namespace AIThread.Service.Databases;

public sealed partial class DatabaseQueryService
{
    // Deletes the rows under a stream of keys (keys.Columns are the key columns), one batch of keys per DELETE.
    private async Task<int> DeleteKeysAsync(DbConnection connection, DbTransaction transaction, DatabaseTarget database, SnapshotHeader keys,
        IAsyncEnumerable<Cell[]> stream, CancellationToken cancellationToken)
    {
        var provider = database.Provider;
        var names = keys.Columns.Select(c => c.Name).ToList();
        var table = SqlDialect.QuoteTable(provider, keys.Table);
        var size = KeyBatchSize(names.Count);
        var batch = new List<Cell[]>(size);
        var deleted = 0;

        async Task FlushAsync()
        {
            if (batch.Count == 0) return;
            await using var command = Command(connection, transaction, $"DELETE FROM {table} WHERE {KeyPredicate(provider, names, batch.Count)}",
                [.. batch.SelectMany(k => k.Select(c => c.ToValue()))]);
            deleted += await command.ExecuteNonQueryAsync(cancellationToken);
            batch.Clear();
        }

        await foreach (var key in stream.WithCancellation(cancellationToken))
        {
            batch.Add(key);
            if (batch.Count >= size) await FlushAsync();
        }
        await FlushAsync();
        return deleted;
    }
}

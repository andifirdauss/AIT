using System.Data.Common;
using System.Runtime.CompilerServices;
using AIThread.Common.Enums;

namespace AIThread.Service.Databases;

public sealed partial class DatabaseQueryService
{
    // The current rows (columns of shape) for a stream of keys (shape.KeysOnly() order), one batch of keys per SELECT.
    // Keys whose row is gone yield nothing.
    private async IAsyncEnumerable<Cell[]> LookupAsync(DbConnection connection, DbTransaction? transaction, MemoryStoreProvider provider, SnapshotHeader shape,
        IAsyncEnumerable<Cell[]> keys, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var keyColumns = shape.KeyIndexes.Select(i => shape.Columns[i].Name).ToList();
        var size = KeyBatchSize(keyColumns.Count);
        var table = SqlDialect.QuoteTable(provider, shape.Table);
        var batch = new List<Cell[]>(size);

        await foreach (var key in keys.WithCancellation(cancellationToken))
        {
            batch.Add(key);
            if (batch.Count < size) continue;
            await foreach (var row in ReadRowsAsync(connection, transaction, provider, shape.Columns, table, KeyPredicate(provider, keyColumns, batch.Count),
                               [.. batch.SelectMany(k => k.Select(c => c.ToValue()))], cancellationToken))
                yield return row;
            batch.Clear();
        }
        if (batch.Count == 0) yield break;
        await foreach (var row in ReadRowsAsync(connection, transaction, provider, shape.Columns, table, KeyPredicate(provider, keyColumns, batch.Count),
                           [.. batch.SelectMany(k => k.Select(c => c.ToValue()))], cancellationToken))
            yield return row;
    }
}

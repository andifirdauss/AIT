using System.Runtime.CompilerServices;

namespace AIThread.Service.Databases;

public sealed partial class DatabaseQueryService
{
    private static async IAsyncEnumerable<Cell[]> KeysOf(SnapshotHeader shape, IAsyncEnumerable<Cell[]> rows, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var row in rows.WithCancellation(cancellationToken)) yield return shape.KeyCells(row);
    }
}

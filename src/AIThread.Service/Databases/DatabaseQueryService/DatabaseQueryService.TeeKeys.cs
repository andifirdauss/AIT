using System.Runtime.CompilerServices;

namespace AIThread.Service.Databases;

public sealed partial class DatabaseQueryService
{
    // One pass over full rows: each is added to checksum, its key cells go on (to a comparison file).
    private static async IAsyncEnumerable<Cell[]> TeeKeys(SnapshotHeader shape, IAsyncEnumerable<Cell[]> rows, RowChecksum checksum,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var row in rows.WithCancellation(cancellationToken))
        {
            checksum.Add(row);
            yield return shape.KeyCells(row);
        }
    }
}

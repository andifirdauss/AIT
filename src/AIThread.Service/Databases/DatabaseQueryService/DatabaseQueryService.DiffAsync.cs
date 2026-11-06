namespace AIThread.Service.Databases;

public sealed partial class DatabaseQueryService
{
    // What a change did to the rows compared in scratch (Backup = before, Current = after), by primary key: how many rows
    // differ, and the first few with their changed values.
    private static async Task<(int Changed, IReadOnlyList<RowChange> Sample)> DiffAsync(DiffScratch scratch, SnapshotHeader shape, CancellationToken cancellationToken)
    {
        var total = await scratch.CountChangedAsync(cancellationToken)
                    + await scratch.CountOnlyInAsync(DiffScratch.Side.Backup, cancellationToken)
                    + await scratch.CountOnlyInAsync(DiffScratch.Side.Current, cancellationToken);
        var compare = shape.CompareIndexes;
        var sample = new List<RowChange>();

        await foreach (var (before, after) in scratch.ChangedAsync(cancellationToken))
        {
            if (sample.Count >= SampleRows) break;
            sample.Add(new RowChange("updated", shape.KeyCells(before), compare.Where(i => before[i].Canonical != after[i].Canonical)
                .Select(i => (shape.Columns[i].Name, (Cell?)before[i], (Cell?)after[i])).ToList()));
        }
        if (sample.Count < SampleRows)
        {
            await foreach (var row in scratch.OnlyInAsync(DiffScratch.Side.Backup, cancellationToken))
            {
                if (sample.Count >= SampleRows) break;
                sample.Add(new RowChange("deleted", shape.KeyCells(row), compare.Where(i => !shape.Columns[i].IsKey).Take(6)
                    .Select(i => (shape.Columns[i].Name, (Cell?)row[i], (Cell?)null)).ToList()));
            }
        }
        if (sample.Count < SampleRows)
        {
            await foreach (var row in scratch.OnlyInAsync(DiffScratch.Side.Current, cancellationToken))
            {
                if (sample.Count >= SampleRows) break;
                sample.Add(Inserted(shape, row));
            }
        }
        return ((int)Math.Min(int.MaxValue, total), sample);
    }
}

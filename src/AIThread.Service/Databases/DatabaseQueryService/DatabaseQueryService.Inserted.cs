namespace AIThread.Service.Databases;

public sealed partial class DatabaseQueryService
{
    // A row the change added, as the sample shows it: its key and its first few values.
    private static RowChange Inserted(SnapshotHeader shape, Cell[] row) =>
        new("inserted", shape.KeyCells(row), shape.CompareIndexes.Where(i => !shape.Columns[i].IsKey).Take(6)
            .Select(i => (shape.Columns[i].Name, (Cell?)null, (Cell?)row[i])).ToList());
}

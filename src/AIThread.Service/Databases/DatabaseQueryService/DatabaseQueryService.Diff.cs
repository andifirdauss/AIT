namespace AIThread.Service.Databases;

public sealed partial class DatabaseQueryService
{
    // What a change did to the table, by primary key: how many rows differ, and the first few with their changed values.
    private static (int Changed, IReadOnlyList<RowChange> Sample) Diff(TableSnapshot before, TableSnapshot after)
    {
        var old = before.Rows.ToDictionary(before.KeyOf, StringComparer.Ordinal);
        var now = after.Rows.ToDictionary(after.KeyOf, StringComparer.Ordinal);
        var keys = before.KeyIndexes;
        var compare = before.CompareIndexes;
        var sample = new List<RowChange>();
        var changed = 0;

        void Add(string kind, Cell[] keyRow, IReadOnlyList<(string, Cell?, Cell?)> values)
        {
            changed++;
            if (sample.Count < SampleRows) sample.Add(new RowChange(kind, keys.Select(k => keyRow[k]).ToList(), values));
        }

        foreach (var (key, row) in old)
        {
            if (!now.TryGetValue(key, out var newRow))
            {
                Add("deleted", row, compare.Where(i => !before.Columns[i].IsKey).Take(6).Select(i => (before.Columns[i].Name, (Cell?)row[i], (Cell?)null)).ToList());
                continue;
            }
            if (before.RowHash(row) == after.RowHash(newRow)) continue;
            Add("updated", row, compare.Where(i => row[i].Canonical != newRow[i].Canonical)
                .Select(i => (before.Columns[i].Name, (Cell?)row[i], (Cell?)newRow[i])).ToList());
        }
        foreach (var (key, row) in now)
        {
            if (old.ContainsKey(key)) continue;
            Add("inserted", row, compare.Where(i => !after.Columns[i].IsKey).Take(6).Select(i => (after.Columns[i].Name, (Cell?)null, (Cell?)row[i])).ToList());
        }
        return (changed, sample);
    }
}

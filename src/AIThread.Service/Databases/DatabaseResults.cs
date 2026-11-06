namespace AIThread.Service.Databases;

// Rows of a read, at most the limit asked (Truncated: there were more).
public sealed record DbQueryResult(IReadOnlyList<string> Columns, IReadOnlyList<Cell[]> Rows, bool Truncated, TimeSpan Elapsed);

public sealed record TableDescription(TableName Table, IReadOnlyList<SnapshotColumn> Columns, long RowCount)
{
    public IReadOnlyList<string> PrimaryKey => Columns.Where(c => c.IsKey).Select(c => c.Name).ToList();
}

// One row a change touched: Kind updated / inserted / deleted; Values: the columns that differ (before, after).
public sealed record RowChange(string Kind, IReadOnlyList<Cell> Key, IReadOnlyList<(string Column, Cell? Before, Cell? After)> Values);

public sealed record RestoreCounts(int Updated, int Inserted, int Deleted)
{
    public int Total => Updated + Inserted + Deleted;
}

// The rolled-back run of a change: rows it affected, a sample of what it changed, and whether restoring the backup in
// the same transaction brought the table back exactly (RestoreVerified). Nothing of it was committed.
public sealed record DryRunResult(int Affected, IReadOnlyList<RowChange> Sample, int ChangedRows, RestoreCounts Restore, bool RestoreVerified, string? Problem);

public sealed record DatabaseTestOutcome(bool Success, long LatencyMs, string? ServerVersion, string? Error);

// The change tried on a local SQLite copy of the touched tables (a Prod rehearsal); Problem says why it could not run.
public sealed record RehearsalResult(int Affected, int ChangedRows, IReadOnlyList<RowChange> Sample, string? Problem);

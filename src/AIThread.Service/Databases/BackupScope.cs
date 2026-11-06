namespace AIThread.Service.Databases;

public enum BackupScopeKind
{
    // Every row of the table.
    Table = 1,

    // Only the rows the statement's WHERE matches (Source + Filter, taken verbatim from the statement).
    Rows = 2,

    // None: an INSERT changes no existing row; the keys it adds are recorded when it runs, and a restore deletes them.
    Insert = 3
}

// What a backup holds for one write statement (SqlStatement.Classify decides). Always enough to undo the statement:
// whatever cannot be narrowed exactly is backed up as the whole table.
public sealed record BackupScope(BackupScopeKind Kind, string? Source = null, string? Filter = null)
{
    public static readonly BackupScope WholeTable = new(BackupScopeKind.Table);

    public static readonly BackupScope NewRows = new(BackupScopeKind.Insert);

    // source: the table as the statement names it (with its alias); filter: the WHERE condition.
    public static BackupScope Matching(string source, string filter) => new(BackupScopeKind.Rows, source, filter);
}

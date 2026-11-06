using System.Text;
using AIThread.Common.Enums;
using AIThread.Data.Memory;
using AIThread.Service.Interfaces;

namespace AIThread.Service.Databases;

// What the database tools hand the model (and the developer's approval card): Markdown tables in which sensitive
// columns are placeholders (SecretGuard.Protect) and every other value went through secret masking.
public static class DatabaseToolText
{
    private const int MaxCellChars = 200;

    public static string Databases(IReadOnlyList<DatabaseTarget> databases)
    {
        if (databases.Count == 0) return "This project has no databases set up (Project > Detail → Database).";
        var text = new StringBuilder("| name | engine | environment | changes | masked columns | about |\n|---|---|---|---|---|---|\n");
        foreach (var db in databases)
        {
            var changes = !db.AllowWrites ? "read only" : db.IsProd ? "with approval, rehearsal suggested" : "with approval";
            text.Append("| ").Append(db.Name).Append(" | ").Append(MemoryStoreConnection.ProviderName(db.Provider)).Append(" | ").Append(db.Environment)
                .Append(" | ").Append(changes).Append(" | ").Append(db.MaskedColumns.Count == 0 ? "auto" : "auto + " + string.Join(", ", db.MaskedColumns))
                .Append(" | ").Append(Escape(db.Description ?? string.Empty)).Append(" |\n");
        }
        return text.ToString();
    }

    public static string Rows(DbQueryResult result, DatabaseTarget database, ISecretGuard secrets)
    {
        if (result.Columns.Count == 0) return "The statement returned no columns.";
        var sensitive = result.Columns.Select(c => SensitiveColumns.IsSensitive(c, database.MaskedColumns)).ToArray();
        var text = new StringBuilder();
        text.Append("| ").Append(string.Join(" | ", result.Columns.Select(Escape))).Append(" |\n");
        text.Append('|').Append(string.Concat(result.Columns.Select(_ => "---|"))).Append('\n');
        foreach (var row in result.Rows)
        {
            text.Append("| ");
            for (var i = 0; i < row.Length; i++)
            {
                if (i > 0) text.Append(" | ");
                text.Append(Escape(Value(row[i], sensitive[i] ? result.Columns[i] : null, secrets)));
            }
            text.Append(" |\n");
        }
        text.Append('\n').Append(result.Rows.Count).Append(result.Rows.Count == 1 ? " row" : " rows");
        if (result.Truncated) text.Append(" (more rows exist: narrow the WHERE, aggregate, or raise max_rows)");
        text.Append($", {result.Elapsed.TotalMilliseconds:0} ms.");
        if (sensitive.Any(s => s))
            text.Append("\nColumns ").Append(string.Join(", ", result.Columns.Where((_, i) => sensitive[i])))
                .Append(" are masked: the same value always has the same placeholder, and a placeholder written in a change_database statement is replaced by the real value before it runs.");
        return text.ToString();
    }

    public static string Describe(TableDescription table, DatabaseTarget database)
    {
        var text = new StringBuilder($"Table {table.Table.Display} on {database.Name} ({MemoryStoreConnection.ProviderName(database.Provider)}), {table.RowCount} rows.\n\n");
        text.Append("| column | type | null | key | notes |\n|---|---|---|---|---|\n");
        foreach (var column in table.Columns)
        {
            var notes = new List<string>();
            if (column.IsIdentity) notes.Add("identity");
            if (!column.Writable) notes.Add("computed");
            if (SensitiveColumns.IsSensitive(column.Name, database.MaskedColumns)) notes.Add("masked");
            text.Append("| ").Append(column.Name).Append(" | ").Append(column.Type).Append(" | ").Append(column.AllowNull ? "yes" : "no")
                .Append(" | ").Append(column.IsKey ? "PK" : string.Empty).Append(" | ").Append(string.Join(", ", notes)).Append(" |\n");
        }
        if (table.PrimaryKey.Count == 0) text.Append("\nNo primary key: change_database cannot back this table up row by row, so it cannot change it.");
        return text.ToString();
    }

    // The dry test as the developer and the model read it: rows affected, a sample of what changes, the restore test.
    public static string DryRun(DryRunResult dry, DatabaseTarget database, ISecretGuard secrets)
    {
        var text = new StringBuilder();
        text.Append($"Dry test (rolled back): {dry.Affected} row(s) affected, {dry.ChangedRows} row(s) differ.\n");
        foreach (var change in dry.Sample)
        {
            var key = string.Join("/", change.Key.Select(k => k.Text ?? "NULL"));
            var values = string.Join(", ", change.Values.Select(v =>
                $"{v.Column}: {Show(v.Before, v.Column, database, secrets)} → {Show(v.After, v.Column, database, secrets)}"));
            text.Append($"- {change.Kind} key {secrets.Redact(key)}: {values}\n");
        }
        if (dry.ChangedRows > dry.Sample.Count) text.Append($"- … and {dry.ChangedRows - dry.Sample.Count} more\n");
        text.Append(dry.RestoreVerified
            ? $"Restore test: passed ({dry.Restore.Updated} changed back, {dry.Restore.Inserted} inserted again, {dry.Restore.Deleted} deleted; the rows matched the backup).\n"
            : $"Restore test: FAILED — {dry.Problem}\n");
        return text.ToString();
    }

    // The backup behind a change: what it covers, its size and how long it and the dry test took (the dry test is about
    // how long the real change holds its locks).
    public static string Backup(StoredBackup stored, DryRunResult dry)
    {
        var scope = stored.Snapshot.Header.Scope.Kind switch
        {
            BackupScopeKind.Rows => "the rows the WHERE matches",
            BackupScopeKind.Insert => "none yet: the keys of the new rows are recorded when it runs",
            _ => "the whole table"
        };
        var made = stored.Elapsed.TotalSeconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
        var tested = dry.Elapsed.TotalSeconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
        var held = dry.Hold.TotalSeconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
        return $"Backup: {stored.Snapshot.Rows} row(s) ({scope}), {DiskSpace.Format(stored.Snapshot.Bytes)}, made in {made} s; the dry test took {tested} s " +
               $"(the real change holds its transaction about {held} s).\n";
    }

    private static string Show(Cell? cell, string column, DatabaseTarget database, ISecretGuard secrets) =>
        cell is null ? "—" : Value(cell, SensitiveColumns.IsSensitive(column, database.MaskedColumns) ? column : null, secrets);

    // sensitiveColumn: the value becomes that column's placeholder.
    private static string Value(Cell cell, string? sensitiveColumn, ISecretGuard secrets)
    {
        if (cell.Tag == "null") return "NULL";
        if (cell.Tag == "x") return $"<binary {Convert.FromBase64String(cell.Text!).Length} bytes>";
        var text = cell.Tag == "b" ? (cell.Text == "1" ? "true" : "false") : cell.Text ?? string.Empty;
        if (sensitiveColumn is not null) return secrets.Protect(SensitiveColumns.Label(sensitiveColumn), text);
        text = secrets.Redact(text);
        return text.Length > MaxCellChars ? text[..MaxCellChars] + "…" : text;
    }

    private static string Escape(string text) => text.Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");

    public static string EnvironmentLabel(DatabaseEnvironment environment) => environment switch
    {
        DatabaseEnvironment.Prod => "PROD",
        DatabaseEnvironment.Staging => "Staging",
        _ => "Dev"
    };
}

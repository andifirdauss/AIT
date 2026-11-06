using System.Data.Common;

namespace AIThread.Service.Databases;

public sealed partial class DatabaseQueryService
{
    // Brings the table back to the backup inside the caller's transaction, by primary key: rows added since are deleted,
    // rows that differ get their old values back, rows that are gone are inserted again. Deletes first, so a restored
    // row never collides with one the change added under a unique value.
    private async Task<RestoreCounts> ApplyRestoreAsync(DbConnection connection, DbTransaction transaction, DatabaseTarget database, TableSnapshot backup,
        CancellationToken cancellationToken)
    {
        var provider = database.Provider;
        var schema = await ReadSchemaAsync(connection, transaction, database, backup.Table, cancellationToken);
        var names = backup.Columns.Select(c => c.Name).ToList();
        if (!schema.Columns.Select(c => c.Name).SequenceEqual(names, StringComparer.OrdinalIgnoreCase))
            throw new DatabaseToolException($"the columns of '{backup.Table.Display}' changed since the backup, so it cannot be restored automatically");

        var current = await ReadSnapshotAsync(connection, transaction, database, schema, cancellationToken);
        var before = backup.Rows.ToDictionary(backup.KeyOf, StringComparer.Ordinal);
        var now = current.Rows.ToDictionary(current.KeyOf, StringComparer.Ordinal);
        var keys = backup.KeyIndexes;
        var table = SqlDialect.QuoteTable(provider, backup.Table);
        var where = string.Join(" AND ", keys.Select((k, n) => $"{SqlDialect.Quote(provider, names[k])} = {SqlDialect.Parameter(provider, n)}"));

        var deleted = 0;
        foreach (var (key, row) in now)
        {
            if (before.ContainsKey(key)) continue;
            await using var command = Command(connection, transaction, $"DELETE FROM {table} WHERE {where}", [.. keys.Select(k => row[k].ToValue())]);
            deleted += await command.ExecuteNonQueryAsync(cancellationToken);
        }

        var writable = backup.Columns.Select((c, i) => (c, i)).Where(x => x.c.Writable && !x.c.IsKey && !x.c.IsIdentity).Select(x => x.i).ToList();
        var updated = 0;
        foreach (var (key, row) in before)
        {
            if (!now.TryGetValue(key, out var currentRow) || current.RowHash(currentRow) == backup.RowHash(row) || writable.Count == 0) continue;
            var set = string.Join(", ", writable.Select((c, n) => $"{SqlDialect.Quote(provider, names[c])} = {SqlDialect.Parameter(provider, n)}"));
            var keyWhere = string.Join(" AND ", keys.Select((k, n) => $"{SqlDialect.Quote(provider, names[k])} = {SqlDialect.Parameter(provider, writable.Count + n)}"));
            await using var command = Command(connection, transaction, $"UPDATE {table} SET {set} WHERE {keyWhere}",
                [.. writable.Select(c => row[c].ToValue()), .. keys.Select(k => row[k].ToValue())]);
            updated += await command.ExecuteNonQueryAsync(cancellationToken);
        }

        var missing = before.Where(p => !now.ContainsKey(p.Key)).Select(p => p.Value).ToList();
        var inserted = 0;
        if (missing.Count > 0)
        {
            var columns = backup.Columns.Select((c, i) => (c, i)).Where(x => x.c.Writable || x.c.IsKey).Select(x => x.i).ToList();
            var (beforeInsert, afterInsert, clause) = SqlDialect.IdentityInsert(provider, backup.Table, columns.Any(i => backup.Columns[i].IsIdentity));
            if (beforeInsert is not null)
            {
                await using var on = Command(connection, transaction, beforeInsert);
                await on.ExecuteNonQueryAsync(cancellationToken);
            }
            var list = string.Join(", ", columns.Select(i => SqlDialect.Quote(provider, names[i])));
            var values = string.Join(", ", columns.Select((_, n) => SqlDialect.Parameter(provider, n)));
            foreach (var row in missing)
            {
                await using var command = Command(connection, transaction, $"INSERT INTO {table} ({list}){clause} VALUES ({values})", [.. columns.Select(i => row[i].ToValue())]);
                inserted += await command.ExecuteNonQueryAsync(cancellationToken);
            }
            if (afterInsert is not null)
            {
                await using var off = Command(connection, transaction, afterInsert);
                await off.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        return new RestoreCounts(updated, inserted, deleted);
    }
}

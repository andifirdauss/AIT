using System.Data.Common;

namespace AIThread.Service.Databases;

public sealed partial class DatabaseQueryService
{
    // Brings the rows the backup covers back to it inside the caller's transaction, by primary key, from a comparison
    // already loaded (scratch: Backup = the backup, Current = the rows now): rows only in Current are deleted (in
    // batches), rows that differ get their old values back, rows only in the backup are inserted again. Deletes first,
    // so a restored row never collides with one the change added under a unique value.
    private async Task<RestoreCounts> ApplyRestoreAsync(DbConnection connection, DbTransaction transaction, DatabaseTarget database, SnapshotHeader header,
        DiffScratch scratch, CancellationToken cancellationToken)
    {
        var provider = database.Provider;
        var names = header.Columns.Select(c => c.Name).ToList();
        var keys = header.KeyIndexes;
        var table = SqlDialect.QuoteTable(provider, header.Table);

        var deleted = await DeleteKeysAsync(connection, transaction, database, header.KeysOnly(),
            KeysOf(header, scratch.OnlyInAsync(DiffScratch.Side.Current, cancellationToken), cancellationToken), cancellationToken);

        var writable = header.Columns.Select((c, i) => (c, i)).Where(x => x.c.Writable && !x.c.IsKey && !x.c.IsIdentity).Select(x => x.i).ToList();
        var updated = 0;
        if (writable.Count > 0)
        {
            var set = string.Join(", ", writable.Select((c, n) => $"{SqlDialect.Quote(provider, names[c])} = {SqlDialect.Parameter(provider, n)}"));
            var keyWhere = string.Join(" AND ", keys.Select((k, n) => $"{SqlDialect.Quote(provider, names[k])} = {SqlDialect.Parameter(provider, writable.Count + n)}"));
            await foreach (var (before, _) in scratch.ChangedAsync(cancellationToken))
            {
                await using var command = Command(connection, transaction, $"UPDATE {table} SET {set} WHERE {keyWhere}",
                    [.. writable.Select(c => before[c].ToValue()), .. keys.Select(k => before[k].ToValue())]);
                updated += await command.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        var columns = header.Columns.Select((c, i) => (c, i)).Where(x => x.c.Writable || x.c.IsKey).Select(x => x.i).ToList();
        var (beforeInsert, afterInsert, clause) = SqlDialect.IdentityInsert(provider, header.Table, columns.Any(i => header.Columns[i].IsIdentity));
        var list = string.Join(", ", columns.Select(i => SqlDialect.Quote(provider, names[i])));
        var values = string.Join(", ", columns.Select((_, n) => SqlDialect.Parameter(provider, n)));
        var inserted = 0;
        var identityOn = false;
        try
        {
            await foreach (var row in scratch.OnlyInAsync(DiffScratch.Side.Backup, cancellationToken))
            {
                if (!identityOn && beforeInsert is not null)
                {
                    await using var on = Command(connection, transaction, beforeInsert);
                    await on.ExecuteNonQueryAsync(cancellationToken);
                    identityOn = true;
                }
                await using var command = Command(connection, transaction, $"INSERT INTO {table} ({list}){clause} VALUES ({values})", [.. columns.Select(i => row[i].ToValue())]);
                inserted += await command.ExecuteNonQueryAsync(cancellationToken);
            }
        }
        finally
        {
            if (identityOn && afterInsert is not null)
            {
                await using var off = Command(connection, transaction, afterInsert);
                await off.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        return new RestoreCounts(updated, inserted, deleted);
    }
}

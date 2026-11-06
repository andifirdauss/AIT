using System.Data.Common;
using System.Diagnostics;

namespace AIThread.Service.Databases;

public sealed partial class DatabaseQueryService
{
    // The dry test of an INSERT: the table's keys before and after are compared through a comparison file; the new keys
    // are the rows it added. None of the existing keys may be gone, and the new keys must be exactly the rows it
    // affected (no trigger or other session adding rows). The restore test deletes the new rows; the whole table must
    // then be as it was before (streamed checksum), so nothing else was touched.
    private async Task<DryRunResult> DryRunInsertAsync(DbConnection connection, DbTransaction transaction, DatabaseTarget database, string sql, BackupSnapshot backup,
        CancellationToken cancellationToken)
    {
        var hold = Stopwatch.StartNew();
        var provider = database.Provider;
        var header = backup.Header;
        var keys = header.KeysOnly();
        var table = SqlDialect.QuoteTable(provider, header.Table);
        var none = new RestoreCounts(0, 0, 0);

        var before = new RowChecksum(header);
        await using var scratch = await OpenScratchAsync(keys, cancellationToken);
        await scratch.LoadAsync(DiffScratch.Side.Backup,
            TeeKeys(header, ReadRowsAsync(connection, transaction, provider, header.Columns, table, null, [], cancellationToken), before, cancellationToken), cancellationToken);

        int affected;
        try
        {
            await using var command = Command(connection, transaction, sql);
            affected = await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (DbException ex)
        {
            return new DryRunResult(0, [], 0, none, false, $"the change failed in the dry test: {ex.Message}");
        }

        await scratch.LoadAsync(DiffScratch.Side.Current, ReadRowsAsync(connection, transaction, provider, keys.Columns, table, null, [], cancellationToken), cancellationToken);
        var held = hold.Elapsed;
        var gone = await scratch.CountOnlyInAsync(DiffScratch.Side.Backup, cancellationToken);
        if (gone > 0)
            return new DryRunResult(affected, [], 0, none, false,
                $"the INSERT removed or re-keyed {gone} existing rows (a trigger or a REPLACE-like insert); AIT backs an INSERT up only by the keys it adds, so it does not run it this way",
                Hold: held);
        var added = await scratch.CountOnlyInAsync(DiffScratch.Side.Current, cancellationToken);
        var addedRows = (int)Math.Min(int.MaxValue, added);
        if (added != affected)
            return new DryRunResult(affected, [], addedRows, none, false,
                $"the INSERT reported {affected} rows but {added} new keys appeared (a trigger or another session adds rows to the table), so the rows it adds could not be told apart for a restore",
                Hold: held);

        var first = new List<Cell[]>();
        await foreach (var key in scratch.OnlyInAsync(DiffScratch.Side.Current, cancellationToken))
        {
            first.Add(key);
            if (first.Count >= SampleRows) break;
        }
        var sample = new List<RowChange>();
        await foreach (var row in LookupAsync(connection, transaction, provider, header, first.ToAsyncEnumerable(), cancellationToken)) sample.Add(Inserted(header, row));

        int deleted;
        try
        {
            deleted = await DeleteKeysAsync(connection, transaction, database, keys, scratch.OnlyInAsync(DiffScratch.Side.Current, cancellationToken), cancellationToken);
        }
        catch (DbException ex)
        {
            return new DryRunResult(affected, sample, addedRows, none, false, $"the restore test failed: {ex.Message}", Hold: held);
        }

        var after = await RowChecksum.OfAsync(header, ReadRowsAsync(connection, transaction, provider, header.Columns, table, null, [], cancellationToken), cancellationToken);
        var verified = after.Value == before.Value;
        return new DryRunResult(affected, sample, addedRows, new RestoreCounts(0, 0, deleted), verified,
            verified ? null : "after deleting the new rows the table was not as before (a trigger changes other rows, or someone else wrote to the table meanwhile), so a restore could not be trusted",
            Hold: held);
    }
}

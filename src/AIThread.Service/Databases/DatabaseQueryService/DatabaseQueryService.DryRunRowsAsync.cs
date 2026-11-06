using System.Data.Common;
using System.Diagnostics;

namespace AIThread.Service.Databases;

public sealed partial class DatabaseQueryService
{
    // The dry test of a whole-table or WHERE backup. First, as the real run does, the rows the backup covers must still
    // be the backup. The statement may not affect more rows than a WHERE backup holds, and after the restore test the
    // backed-up rows must match the backup again; for a WHERE backup the whole table must also be as it was before the
    // statement (streamed checksums), so a trigger, a cascade or a changed key that reaches other rows stops the change.
    private async Task<DryRunResult> DryRunRowsAsync(DbConnection connection, DbTransaction transaction, DatabaseTarget database, string sql, BackupSnapshot backup,
        CancellationToken cancellationToken)
    {
        var hold = Stopwatch.StartNew();
        var provider = database.Provider;
        var header = backup.Header;
        var narrow = header.Scope.Kind == BackupScopeKind.Rows;
        var table = SqlDialect.QuoteTable(provider, header.Table);
        var none = new RestoreCounts(0, 0, 0);

        IAsyncEnumerable<Cell[]> WholeTable() => ReadRowsAsync(connection, transaction, provider, header.Columns, table, null, [], cancellationToken);

        var (from, where) = Source(provider, header);
        var covered = await RowChecksum.OfAsync(header, ReadRowsAsync(connection, transaction, provider, header.Columns, from, where, [], cancellationToken), cancellationToken);
        if (covered.Value != backup.Checksum)
            return new DryRunResult(0, [], 0, none, false,
                "the rows the backup covers changed since it was made (someone else wrote to the table); call change_database again for a fresh backup");
        var tableBefore = narrow ? (await RowChecksum.OfAsync(header, WholeTable(), cancellationToken)).Value : backup.Checksum;

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
        var held = hold.Elapsed;
        if (narrow && affected > backup.Rows)
            return new DryRunResult(affected, [], 0, none, false,
                $"the statement affected {affected} rows, but its WHERE matched only {backup.Rows} in the backup: it reaches rows outside the backup, so AIT does not run it", Hold: held);

        await using var scratch = await OpenScratchAsync(header, cancellationToken);
        await scratch.LoadAsync(DiffScratch.Side.Backup, backup.ReadRowsAsync(cancellationToken), cancellationToken);
        await scratch.LoadAsync(DiffScratch.Side.Current, CurrentRowsAsync(connection, transaction, database, backup, cancellationToken), cancellationToken);
        var (changed, sample) = await DiffAsync(scratch, header, cancellationToken);

        RestoreCounts restore;
        try
        {
            restore = await ApplyRestoreAsync(connection, transaction, database, header, scratch, cancellationToken);
        }
        catch (Exception ex) when (ex is DbException or DatabaseToolException)
        {
            return new DryRunResult(affected, sample, changed, none, false, $"the restore test failed: {ex.Message}", Hold: held);
        }

        var restored = await RowChecksum.OfAsync(header, CurrentRowsAsync(connection, transaction, database, backup, cancellationToken), cancellationToken);
        if (restored.Value != backup.Checksum)
            return new DryRunResult(affected, sample, changed, restore, false,
                "after the restore test the rows did not match the backup (a trigger or a default may change rows), so a restore could not be trusted", Hold: held);
        if (narrow && (await RowChecksum.OfAsync(header, WholeTable(), cancellationToken)).Value != tableBefore)
            return new DryRunResult(affected, sample, changed, restore, false,
                "after the restore test rows outside the backup were not as before (a trigger, a cascade or a changed primary key reaches them, or someone else wrote to the table meanwhile), so restoring only the matched rows could not be trusted", Hold: held);
        return new DryRunResult(affected, sample, changed, restore, true, null, Hold: held);
    }
}

using AIThread.Common.Enums;
using AIThread.Data.Memory;
using Microsoft.Data.Sqlite;

namespace AIThread.Service.Databases;

public sealed partial class DatabaseQueryService
{
    // The tables are streamed from the target into a fresh SQLite file (named without their schema, as sqlite_sql names
    // them), with no row limit and the same disk reserve as a backup; the change runs there in a transaction, is compared
    // with the copy and rolled back; the file is deleted at the end, so no copied data stays on disk.
    public async Task<RehearsalResult> RehearseAsync(DatabaseTarget database, IReadOnlyList<TableName> tables, TableName target, string sqliteSql, Guid runId,
        CancellationToken cancellationToken = default)
    {
        var file = Path.Combine(Root, "db-rehearsal", $"{runId:N}.db");
        var check = SqlStatement.Classify(sqliteSql, MemoryStoreProvider.Sqlite);
        if (check.Kind != SqlStatementKind.Write || check.Target is null)
            return new RehearsalResult(0, 0, [], check.Reason ?? "sqlite_sql must be one INSERT, UPDATE, DELETE or MERGE");
        if (!string.Equals(check.Target.Name, target.Name, StringComparison.OrdinalIgnoreCase))
            return new RehearsalResult(0, 0, [], $"sqlite_sql changes '{check.Target.Display}', but the change is on '{target.Name}'");

        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        if (File.Exists(file)) File.Delete(file);
        var local = new DatabaseTarget(Guid.Empty, Guid.Empty, "rehearsal", new MemoryStoreConnection(MemoryStoreProvider.Sqlite, Database: file), DatabaseEnvironment.Dev,
            [], 50, true, null);
        try
        {
            await using (var copy = await OpenAsync(local, cancellationToken))
            await using (var source = await OpenAsync(database, cancellationToken))
            {
                foreach (var table in tables.DistinctBy(t => t.Name, StringComparer.OrdinalIgnoreCase))
                {
                    var schema = await ReadSchemaAsync(source, null, database, table, cancellationToken);
                    if (string.Equals(table.Name, target.Name, StringComparison.OrdinalIgnoreCase) && !schema.Columns.Any(c => c.IsKey))
                        return new RehearsalResult(0, 0, [], $"the table '{table.Display}' has no primary key, so the rehearsal cannot compare its rows");
                    await LoadCopyAsync(source, database, schema, copy, file, cancellationToken);
                }
            }

            await using var rehearsal = await OpenAsync(local, cancellationToken);
            var localTable = new TableName(null, tables.First(t => string.Equals(t.Name, target.Name, StringComparison.OrdinalIgnoreCase)).Name);
            var localSchema = await ReadSchemaAsync(rehearsal, null, local, localTable, cancellationToken);
            var shape = new SnapshotHeader(localTable, localSchema.Columns, BackupScope.WholeTable, DateTime.UtcNow);
            var quoted = SqlDialect.QuoteTable(MemoryStoreProvider.Sqlite, localTable);

            await using var scratch = await OpenScratchAsync(shape, cancellationToken);
            await scratch.LoadAsync(DiffScratch.Side.Backup, ReadRowsAsync(rehearsal, null, MemoryStoreProvider.Sqlite, shape.Columns, quoted, null, [], cancellationToken), cancellationToken);
            await using var transaction = await rehearsal.BeginTransactionAsync(cancellationToken);
            try
            {
                int affected;
                try
                {
                    await using var command = Command(rehearsal, transaction, sqliteSql);
                    affected = await command.ExecuteNonQueryAsync(cancellationToken);
                }
                catch (SqliteException ex)
                {
                    return new RehearsalResult(0, 0, [], $"sqlite_sql failed on the local copy: {ex.Message}");
                }
                await scratch.LoadAsync(DiffScratch.Side.Current, ReadRowsAsync(rehearsal, transaction, MemoryStoreProvider.Sqlite, shape.Columns, quoted, null, [], cancellationToken),
                    cancellationToken);
                var (changed, sample) = await DiffAsync(scratch, shape, cancellationToken);
                return new RehearsalResult(affected, changed, sample, null);
            }
            finally
            {
                await RollbackQuietlyAsync(transaction);
            }
        }
        catch (DatabaseToolException ex)
        {
            return new RehearsalResult(0, 0, [], ex.Message);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try
            {
                if (File.Exists(file)) File.Delete(file);
            }
            catch (IOException)
            {
                // Released on the next start; the copy holds nothing a backup does not.
            }
        }
    }
}

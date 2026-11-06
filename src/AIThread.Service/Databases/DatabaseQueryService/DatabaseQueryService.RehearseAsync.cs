using AIThread.Common.Enums;
using AIThread.Data.Memory;
using Microsoft.Data.Sqlite;

namespace AIThread.Service.Databases;

public sealed partial class DatabaseQueryService
{
    // The copies become tables of a fresh SQLite file (named without their schema, as sqlite_sql names them); the change
    // runs there in a transaction, is compared with the copy and rolled back; the file is deleted at the end, so no
    // copied data stays on disk.
    public async Task<RehearsalResult> RehearseAsync(IReadOnlyList<TableSnapshot> copies, TableName target, string sqliteSql, Guid runId,
        CancellationToken cancellationToken = default)
    {
        var root = string.IsNullOrWhiteSpace(Limits.Directory)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AIThread")
            : Limits.Directory;
        var file = Path.Combine(root, "db-rehearsal", $"{runId:N}.db");
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
            await using (var connection = await OpenAsync(local, cancellationToken))
            {
                foreach (var copy in copies.DistinctBy(c => c.Table.Name, StringComparer.OrdinalIgnoreCase))
                    await LoadCopyAsync(connection, copy, cancellationToken);
            }

            await using var rehearsal = await OpenAsync(local, cancellationToken);
            var localTable = new TableName(null, copies.First(c => string.Equals(c.Table.Name, target.Name, StringComparison.OrdinalIgnoreCase)).Table.Name);
            var schema = await ReadSchemaAsync(rehearsal, null, local, localTable, cancellationToken);
            var before = await ReadSnapshotAsync(rehearsal, null, local, schema, cancellationToken);
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
                var after = await ReadSnapshotAsync(rehearsal, transaction, local, schema, cancellationToken);
                var (changed, sample) = Diff(before, after);
                return new RehearsalResult(affected, changed, sample, null);
            }
            finally
            {
                await RollbackQuietlyAsync(transaction);
            }
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

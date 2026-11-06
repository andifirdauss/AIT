using AIThread.Service.Databases;
using Microsoft.AspNetCore.DataProtection;

namespace AIThread.Service.Interfaces;

// Talks to a project database (DatabaseTarget): reads, table descriptions, streamed backups, the rolled-back dry test
// of a change together with a restore test, the real execution, and restoring a backup. Rows are never held in memory
// as a whole: backups stream to disk and comparisons go through a temporary file, so no table is too big.
public interface IDatabaseQueryService
{
    // One read statement (SqlStatement.Kind Read), in a read-only transaction where the engine has one, always rolled back.
    Task<DbQueryResult> QueryAsync(DatabaseTarget database, string sql, int maxRows, CancellationToken cancellationToken = default);

    Task<TableDescription> DescribeTableAsync(DatabaseTarget database, TableName table, CancellationToken cancellationToken = default);

    // Writes what scope covers (the whole table, the rows a WHERE matches, or nothing for an INSERT) to a new backup file
    // at path. Refused without a primary key, or when the disk would keep less than the reserve free.
    Task<BackupSnapshot> SnapshotAsync(DatabaseTarget database, TableName table, BackupScope scope, string path, IDataProtector protector,
        CancellationToken cancellationToken = default);

    // Runs the change, restores the backup and checks the rows match it again, all in one transaction that is rolled back.
    Task<DryRunResult> DryRunAsync(DatabaseTarget database, string sql, BackupSnapshot backup, CancellationToken cancellationToken = default);

    // Runs the change and commits only when the backed-up rows are still the backup and it affects exactly expectedRows
    // (the dry test's count); an INSERT's added keys are written beside the backup first.
    Task<int> ExecuteAsync(DatabaseTarget database, string sql, int expectedRows, BackupSnapshot backup, CancellationToken cancellationToken = default);

    // Brings the rows the backup covers back (changed, inserted or deleted since) and commits when they then match.
    Task<RestoreCounts> RestoreAsync(DatabaseTarget database, BackupSnapshot backup, CancellationToken cancellationToken = default);

    // Streams tables of database into a fresh SQLite file of its own (db-rehearsal\<runId>.db), runs sqliteSql there on
    // target, compares, rolls back and deletes the file.
    Task<RehearsalResult> RehearseAsync(DatabaseTarget database, IReadOnlyList<TableName> tables, TableName target, string sqliteSql, Guid runId,
        CancellationToken cancellationToken = default);

    Task<DatabaseTestOutcome> TestConnectionAsync(DatabaseTarget database, CancellationToken cancellationToken = default);
}

using AIThread.Service.Databases;

namespace AIThread.Service.Interfaces;

// Talks to a project database (DatabaseTarget): reads, table descriptions, snapshots for backups, the rolled-back dry
// test of a change together with a restore test, the real execution, and restoring a backup.
public interface IDatabaseQueryService
{
    // One read statement (SqlStatement.Kind Read), in a read-only transaction where the engine has one, always rolled back.
    Task<DbQueryResult> QueryAsync(DatabaseTarget database, string sql, int maxRows, CancellationToken cancellationToken = default);

    Task<TableDescription> DescribeTableAsync(DatabaseTarget database, TableName table, CancellationToken cancellationToken = default);

    // All rows of a table (refused above ProjectDatabaseOptions.BackupMaxRows, or without a primary key when forRestore).
    Task<TableSnapshot> SnapshotAsync(DatabaseTarget database, TableName table, bool forRestore, CancellationToken cancellationToken = default);

    // Runs the change, restores the backup and checks the table matches it, all in one transaction that is rolled back.
    Task<DryRunResult> DryRunAsync(DatabaseTarget database, string sql, TableSnapshot backup, CancellationToken cancellationToken = default);

    // Runs the change and commits only when it affects exactly expectedRows (the dry test's count).
    Task<int> ExecuteAsync(DatabaseTarget database, string sql, int expectedRows, CancellationToken cancellationToken = default);

    // Brings the table back to the backup (rows changed, inserted or deleted since) and commits when it then matches.
    Task<RestoreCounts> RestoreAsync(DatabaseTarget database, TableSnapshot backup, CancellationToken cancellationToken = default);

    // Copies (from SnapshotAsync) into a fresh SQLite file of its own (db-rehearsal\<runId>.db), runs sqliteSql there,
    // compares, rolls back and deletes the file.
    Task<RehearsalResult> RehearseAsync(IReadOnlyList<TableSnapshot> copies, TableName target, string sqliteSql, Guid runId, CancellationToken cancellationToken = default);

    Task<DatabaseTestOutcome> TestConnectionAsync(DatabaseTarget database, CancellationToken cancellationToken = default);
}

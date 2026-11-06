using AIThread.Common.Enums;
using AIThread.Common.Options;
using AIThread.Data.Memory;
using AIThread.Service.Databases;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit.Abstractions;

namespace AIThread.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SerialMemoryCollection
{
    public const string Name = "Serial memory";
}

// A backup has no row limit: a 200,000-row table goes through backup, dry test, execution and restore while the live
// managed memory stays small (rows stream to disk and through a comparison file). Runs alone, so the measurement is
// not disturbed by other tests.
[Collection(SerialMemoryCollection.Name)]
public class DatabaseLargeTableTests : IDisposable
{
    private const int TableRows = 200_000;

    // The old in-memory snapshot needed well over 100 MB live for this table (backup + after + dictionaries).
    private const long MaxLiveBytes = 48L * 1024 * 1024;

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "aithread-tests", $"dbl-{Guid.NewGuid():N}");
    private readonly string _file;
    private readonly DatabaseTarget _target;
    private readonly DatabaseQueryService _service;
    private readonly IDataProtector _protector = new EphemeralDataProtectionProvider().CreateProtector("tests");

    private readonly ITestOutputHelper _output;

    public DatabaseLargeTableTests(ITestOutputHelper output)
    {
        _output = output;
        Directory.CreateDirectory(_folder);
        _file = Path.Combine(_folder, "big.db");
        var options = new AIThreadOptions();
        options.Databases.Directory = _folder;
        options.Databases.BackupFreeSpaceReserveMb = 0;
        options.Databases.CommandTimeoutSeconds = 300;
        _service = new DatabaseQueryService(Options.Create(options), NullLogger<DatabaseQueryService>.Instance);
        _target = new DatabaseTarget(Guid.NewGuid(), Guid.NewGuid(), "big", new MemoryStoreConnection(MemoryStoreProvider.Sqlite, Database: _file),
            DatabaseEnvironment.Dev, [], 50, true, null);
        Exec($"""
            CREATE TABLE Big (Id INTEGER PRIMARY KEY, Name TEXT NOT NULL, Amount NUMERIC NOT NULL, Status TEXT NOT NULL);
            WITH RECURSIVE n(i) AS (SELECT 1 UNION ALL SELECT i + 1 FROM n WHERE i < {TableRows})
            INSERT INTO Big SELECT i, 'customer-' || i || '-' || hex(randomblob(8)), i * 1.5, 'new' FROM n;
            """);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
    }

    private void Exec(string sql)
    {
        using var connection = new SqliteConnection($"Data Source={_file}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private long Scalar(string sql)
    {
        using var connection = new SqliteConnection($"Data Source={_file}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar());
    }

    private Task<BackupSnapshot> BackupAsync(string sql)
    {
        var check = SqlStatement.Classify(sql, MemoryStoreProvider.Sqlite);
        return _service.SnapshotAsync(_target, check.Target!, check.Scope, Path.Combine(_folder, $"{Guid.NewGuid():N}.bak"), _protector);
    }

    // The highest live managed memory (after a full collection) seen while work runs, above what was live before.
    private static async Task<long> PeakLiveBytesAsync(Func<Task> work)
    {
        var baseline = GC.GetTotalMemory(forceFullCollection: true);
        var peak = 0L;
        using var stop = new CancellationTokenSource();
        var sampler = Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested)
            {
                peak = Math.Max(peak, GC.GetTotalMemory(forceFullCollection: true) - baseline);
                try { await Task.Delay(100, stop.Token); } catch (OperationCanceledException) { }
            }
        });
        try
        {
            await work();
        }
        finally
        {
            await stop.CancelAsync();
            await sampler;
        }
        return peak;
    }

    [Fact]
    public async Task A_whole_table_backup_of_200000_rows_runs_and_restores_in_little_memory()
    {
        // LIMIT inside the WHERE makes the matched rows not deterministic: the whole table is backed up.
        const string sql = "UPDATE Big SET Status = 'paid' WHERE Id IN (SELECT Id FROM Big WHERE Id % 20000 = 0 ORDER BY Id LIMIT 10)";
        Assert.Equal(BackupScopeKind.Table, SqlStatement.Classify(sql, MemoryStoreProvider.Sqlite).Scope.Kind);

        BackupSnapshot? backup = null;
        DryRunResult? dry = null;
        RestoreCounts? restored = null;
        var peak = await PeakLiveBytesAsync(async () =>
        {
            backup = await BackupAsync(sql);
            dry = await _service.DryRunAsync(_target, sql, backup);
            Assert.Equal(10, await _service.ExecuteAsync(_target, sql, dry.Affected, backup));
            Assert.Equal(10, Scalar("SELECT COUNT(*) FROM Big WHERE Status = 'paid'"));
            restored = await _service.RestoreAsync(_target, backup);
        });

        Assert.Equal(TableRows, backup!.Rows);
        Assert.Null(dry!.Problem);
        Assert.True(dry.RestoreVerified);
        Assert.Equal(10, dry.ChangedRows);
        Assert.Equal(10, restored!.Updated);
        Assert.Equal(0, Scalar("SELECT COUNT(*) FROM Big WHERE Status = 'paid'"));
        _output.WriteLine($"{TableRows} rows: backup {backup.Bytes / 1024} KB, dry test {dry.Elapsed.TotalSeconds:0.0} s (hold {dry.Hold.TotalSeconds:0.0} s), live managed memory peak {peak / 1024 / 1024.0:0.0} MB");
        Assert.True(peak < MaxLiveBytes, $"live managed memory peaked {peak / 1024 / 1024} MB above the baseline");
        Assert.Empty(Directory.GetFiles(Path.Combine(_folder, "db-work")));
    }

    [Fact]
    public async Task A_narrow_where_on_a_large_table_backs_up_only_the_matched_rows()
    {
        const string sql = "DELETE FROM Big WHERE Id BETWEEN 100 AND 109";
        var backup = await BackupAsync(sql);

        var dry = await _service.DryRunAsync(_target, sql, backup);

        Assert.Equal(BackupScopeKind.Rows, backup.Header.Scope.Kind);
        Assert.Equal(10, backup.Rows);
        Assert.True(dry.RestoreVerified, dry.Problem);
        Assert.Equal(10, dry.Restore.Inserted);
        Assert.Equal(TableRows, Scalar("SELECT COUNT(*) FROM Big"));

        Assert.Equal(10, await _service.ExecuteAsync(_target, sql, dry.Affected, backup));
        Assert.Equal(10, (await _service.RestoreAsync(_target, backup)).Inserted);
        Assert.Equal(TableRows, Scalar("SELECT COUNT(*) FROM Big"));
    }
}

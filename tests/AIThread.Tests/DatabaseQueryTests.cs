using AIThread.Common.Enums;
using AIThread.Common.Options;
using AIThread.Data.Memory;
using AIThread.Service.Databases;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AIThread.Tests;

public class DatabaseQueryTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "aithread-tests", $"dbq-{Guid.NewGuid():N}");
    private readonly string _file;
    private readonly DatabaseTarget _target;
    private readonly DatabaseQueryService _service;
    private readonly IDataProtector _protector = new EphemeralDataProtectionProvider().CreateProtector("tests");

    public DatabaseQueryTests()
    {
        Directory.CreateDirectory(_folder);
        _file = Path.Combine(_folder, "shop.db");
        var options = new AIThreadOptions();
        options.Databases.Directory = _folder;
        options.Databases.BackupFreeSpaceReserveMb = 0;
        _service = new DatabaseQueryService(Options.Create(options), NullLogger<DatabaseQueryService>.Instance);
        _target = new DatabaseTarget(Guid.NewGuid(), Guid.NewGuid(), "shop-dev", new MemoryStoreConnection(MemoryStoreProvider.Sqlite, Database: _file),
            DatabaseEnvironment.Dev, [], 50, true, null);
        Exec("""
            CREATE TABLE Orders (Id INTEGER PRIMARY KEY, Customer TEXT NOT NULL, Email TEXT, Total NUMERIC NOT NULL, Status TEXT NOT NULL);
            INSERT INTO Orders VALUES (1, 'Ana', 'ana@example.com', 10.50, 'new');
            INSERT INTO Orders VALUES (2, 'Budi', 'budi@example.com', 20, 'new');
            INSERT INTO Orders VALUES (3, 'Citra', NULL, 30.25, 'paid');
            CREATE TABLE Logs (Line TEXT);
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

    private string Rows()
    {
        using var connection = new SqliteConnection($"Data Source={_file}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id || ':' || Status || ':' || Total FROM Orders ORDER BY Id";
        using var reader = command.ExecuteReader();
        var rows = new List<string>();
        while (reader.Read()) rows.Add(reader.GetString(0));
        return string.Join(",", rows);
    }

    // The backup change_database makes for sql: what SqlStatement says it must cover, in a file of its own.
    private Task<BackupSnapshot> BackupAsync(string sql)
    {
        var check = SqlStatement.Classify(sql, MemoryStoreProvider.Sqlite);
        return _service.SnapshotAsync(_target, check.Target!, check.Scope, Path.Combine(_folder, $"{Guid.NewGuid():N}.bak"), _protector);
    }

    [Theory]
    [InlineData("SELECT * FROM Orders", SqlStatementKind.Read)]
    [InlineData("WITH c AS (SELECT 1 AS x) SELECT (x + 1) FROM c", SqlStatementKind.Read)]
    [InlineData("select * from Orders where Customer = 'a; DROP TABLE x'", SqlStatementKind.Read)]
    [InlineData("UPDATE Orders SET Status = 'x' WHERE Id = 1;", SqlStatementKind.Write)]
    [InlineData("INSERT INTO dbo.Orders (Id, Customer) VALUES (9, 'z')", SqlStatementKind.Write)]
    [InlineData("DELETE o FROM Orders o JOIN Customers c ON c.Id = o.CustomerId", SqlStatementKind.Write)]
    [InlineData("DROP TABLE Orders", SqlStatementKind.Refused)]
    [InlineData("SELECT 1; DELETE FROM Orders", SqlStatementKind.Refused)]
    [InlineData("SELECT * INTO Copy FROM Orders", SqlStatementKind.Refused)]
    [InlineData("EXEC sp_who", SqlStatementKind.Refused)]
    [InlineData("WITH gone AS (DELETE FROM Logs RETURNING *) UPDATE Orders SET Status = 'x'", SqlStatementKind.Refused)]
    [InlineData("TRUNCATE TABLE Orders", SqlStatementKind.Refused)]
    [InlineData("INSERT ALL INTO Orders (Id) VALUES (1) INTO Logs (Line) VALUES ('x') SELECT 1 FROM dual", SqlStatementKind.Refused)]
    [InlineData("UPDATE Orders SET Status = 'x' OUTPUT deleted.Id INTO Audit WHERE Id = 1", SqlStatementKind.Refused)]
    public void Statements_are_classified_strictly(string sql, SqlStatementKind expected) =>
        Assert.Equal(expected, SqlStatement.Classify(sql, MemoryStoreProvider.SqlServer).Kind);

    [Fact]
    public void Write_target_resolves_aliases_and_folds_unquoted_names()
    {
        Assert.Equal(new TableName(null, "Orders"), SqlStatement.Classify("UPDATE o SET Status = 'x' FROM Orders o WHERE o.Id = 1", MemoryStoreProvider.SqlServer).Target);
        Assert.Equal(new TableName("sales", "Orders"), SqlStatement.Classify("DELETE FROM [sales].[Orders] WHERE Id = 1", MemoryStoreProvider.SqlServer).Target);
        Assert.Equal(new TableName(null, "orders"), SqlStatement.Classify("UPDATE Orders SET status = 'x'", MemoryStoreProvider.PostgreSql).Target);
        Assert.Equal(new TableName(null, "ORDERS"), SqlStatement.Classify("UPDATE orders SET status = 'x'", MemoryStoreProvider.Oracle).Target);
        Assert.Equal(new TableName(null, "Orders"), SqlStatement.Classify("UPDATE \"Orders\" SET status = 'x'", MemoryStoreProvider.PostgreSql).Target);
        Assert.Equal(new TableName(null, "Orders"), SqlStatement.Classify("UPDATE LOW_PRIORITY IGNORE Orders SET Status = 'x' WHERE Id = 1", MemoryStoreProvider.MySql).Target);
    }

    // The WHERE of a plain UPDATE/DELETE becomes the backup's filter, verbatim; anything that cannot be read exactly
    // is backed up as the whole table; a plain INSERT backs up nothing but the keys it adds.
    [Theory]
    [InlineData("UPDATE Orders SET Status = 'x' WHERE Id = 1", BackupScopeKind.Rows, "Orders", "Id = 1")]
    [InlineData("UPDATE [sales].[Orders] SET Status = 'x' WHERE Status = 'a;b' -- note", BackupScopeKind.Rows, "[sales].[Orders]", "Status = 'a;b'")]
    [InlineData("DELETE FROM Orders AS o WHERE o.Total > 10 ORDER BY o.Id LIMIT 5", BackupScopeKind.Rows, "Orders AS o", "o.Total > 10")]
    [InlineData("DELETE TOP (10) FROM Orders WHERE Status = 'new'", BackupScopeKind.Rows, "Orders", "Status = 'new'")]
    [InlineData("UPDATE Orders SET Total = (SELECT MAX(Total) FROM Orders) WHERE Id IN (SELECT OrderId FROM Items)", BackupScopeKind.Rows, "Orders", "Id IN (SELECT OrderId FROM Items)")]
    [InlineData("UPDATE Orders SET Status = 'x'", BackupScopeKind.Table, null, null)]
    [InlineData("UPDATE o SET Status = 'x' FROM Orders o WHERE o.Id = 1", BackupScopeKind.Table, null, null)]
    [InlineData("DELETE o FROM Orders o JOIN Customers c ON c.Id = o.CustomerId WHERE c.Id = 1", BackupScopeKind.Table, null, null)]
    [InlineData("DELETE FROM Orders WHERE Id IN (SELECT TOP 5 Id FROM Orders ORDER BY NEWID())", BackupScopeKind.Table, null, null)]
    [InlineData("WITH c AS (SELECT 1 AS Id) DELETE FROM Orders WHERE Id IN (SELECT Id FROM c)", BackupScopeKind.Table, null, null)]
    [InlineData("MERGE INTO Orders t USING Staging s ON t.Id = s.Id WHEN MATCHED THEN UPDATE SET Status = s.Status", BackupScopeKind.Table, null, null)]
    [InlineData("INSERT INTO Orders (Id, Status) VALUES (9, 'new')", BackupScopeKind.Insert, null, null)]
    [InlineData("INSERT INTO Orders (Id, Status) VALUES (9, 'new') ON CONFLICT (Id) DO UPDATE SET Status = 'x'", BackupScopeKind.Table, null, null)]
    public void Backup_scope_follows_the_statement(string sql, BackupScopeKind kind, string? source, string? filter)
    {
        var scope = SqlStatement.Classify(sql, MemoryStoreProvider.SqlServer).Scope;

        Assert.Equal(kind, scope.Kind);
        Assert.Equal(source, scope.Source);
        Assert.Equal(filter, scope.Filter);
    }

    [Fact]
    public void Sensitive_columns_are_detected_by_name_and_by_list()
    {
        Assert.True(SensitiveColumns.IsSensitive("Email", []));
        Assert.True(SensitiveColumns.IsSensitive("no_telp", []));
        Assert.True(SensitiveColumns.IsSensitive("PasswordHash", []));
        Assert.True(SensitiveColumns.IsSensitive("Nik", []));
        Assert.False(SensitiveColumns.IsSensitive("Status", []));
        Assert.True(SensitiveColumns.IsSensitive("FullName", SensitiveColumns.Parse("fullname, Customer*")));
        Assert.True(SensitiveColumns.IsSensitive("CustomerName", SensitiveColumns.Parse("fullname, Customer*")));
    }

    [Fact]
    public async Task Query_reads_rows_with_a_limit_and_refuses_writes()
    {
        var result = await _service.QueryAsync(_target, "SELECT Id, Customer FROM Orders ORDER BY Id", 2);

        Assert.Equal(["Id", "Customer"], result.Columns);
        Assert.Equal(2, result.Rows.Count);
        Assert.True(result.Truncated);
        await Assert.ThrowsAsync<DatabaseToolException>(() => _service.QueryAsync(_target, "UPDATE Orders SET Status = 'x'", 10));
        Assert.Equal("1:new:10.5,2:new:20,3:paid:30.25", Rows());
    }

    [Fact]
    public async Task Describe_reads_columns_and_primary_key()
    {
        var table = await _service.DescribeTableAsync(_target, new TableName(null, "Orders"));

        Assert.Equal(["Id"], table.PrimaryKey);
        Assert.Equal(3, table.RowCount);
        Assert.Contains(table.Columns, c => c.Name == "Email" && c.AllowNull);
    }

    [Fact]
    public async Task A_where_backup_holds_only_the_matched_rows_and_the_dry_run_restores_them()
    {
        const string sql = "UPDATE Orders SET Status = 'cancelled' WHERE Status = 'new'";
        var backup = await BackupAsync(sql);

        var dry = await _service.DryRunAsync(_target, sql, backup);

        Assert.Equal(BackupScopeKind.Rows, backup.Header.Scope.Kind);
        Assert.Equal(2, backup.Rows);
        Assert.Null(dry.Problem);
        Assert.Equal(2, dry.Affected);
        Assert.Equal(2, dry.ChangedRows);
        Assert.True(dry.RestoreVerified);
        Assert.Equal(2, dry.Restore.Updated);
        Assert.All(dry.Sample, c => Assert.Equal("updated", c.Kind));
        Assert.Equal("1:new:10.5,2:new:20,3:paid:30.25", Rows());
    }

    [Fact]
    public async Task Dry_run_restores_deleted_rows_and_deletes_inserted_ones()
    {
        const string delete = "DELETE FROM Orders WHERE Id = 2";
        const string insert = "INSERT INTO Orders (Id, Customer, Total, Status) VALUES (4, 'Dewi', 5, 'new')";

        var deleted = await _service.DryRunAsync(_target, delete, await BackupAsync(delete));
        var insertBackup = await BackupAsync(insert);
        var inserted = await _service.DryRunAsync(_target, insert, insertBackup);

        Assert.True(deleted.RestoreVerified);
        Assert.Equal(1, deleted.Restore.Inserted);
        Assert.Equal(BackupScopeKind.Insert, insertBackup.Header.Scope.Kind);
        Assert.Equal(0, insertBackup.Rows);
        Assert.True(inserted.RestoreVerified, inserted.Problem);
        Assert.Equal(1, inserted.ChangedRows);
        Assert.Equal(1, inserted.Restore.Deleted);
        Assert.Equal("inserted", Assert.Single(inserted.Sample).Kind);
        Assert.Equal("1:new:10.5,2:new:20,3:paid:30.25", Rows());
    }

    [Fact]
    public async Task Execute_commits_only_the_approved_row_count_and_restore_brings_back_only_the_backed_up_rows()
    {
        const string sql = "UPDATE Orders SET Status = 'cancelled' WHERE Status = 'new'";
        var backup = await BackupAsync(sql);

        await Assert.ThrowsAsync<DatabaseToolException>(() => _service.ExecuteAsync(_target, sql, expectedRows: 1, backup));
        Assert.Equal("1:new:10.5,2:new:20,3:paid:30.25", Rows());

        Assert.Equal(2, await _service.ExecuteAsync(_target, sql, expectedRows: 2, backup));
        Assert.Equal("1:cancelled:10.5,2:cancelled:20,3:paid:30.25", Rows());

        // Someone else changes a row the backup does not cover: the restore leaves it alone.
        Exec("UPDATE Orders SET Status = 'shipped' WHERE Id = 3");
        var counts = await _service.RestoreAsync(_target, backup);
        Assert.Equal(2, counts.Updated);
        Assert.Equal("1:new:10.5,2:new:20,3:shipped:30.25", Rows());
    }

    [Fact]
    public async Task An_insert_records_the_keys_it_added_and_restore_deletes_only_those()
    {
        const string sql = "INSERT INTO Orders (Id, Customer, Total, Status) VALUES (4, 'Dewi', 5, 'new')";
        var backup = await BackupAsync(sql);
        Assert.Equal(new RestoreCounts(0, 0, 0), await _service.RestoreAsync(_target, backup));

        Assert.Equal(1, await _service.ExecuteAsync(_target, sql, expectedRows: 1, backup));
        Assert.True(backup.HasAddedKeys);
        Exec("INSERT INTO Orders VALUES (5, 'Eka', NULL, 7, 'new')");

        var counts = await _service.RestoreAsync(_target, backup);
        Assert.Equal(1, counts.Deleted);
        Assert.Equal("1:new:10.5,2:new:20,3:paid:30.25,5:new:7", Rows());
    }

    [Fact]
    public async Task A_change_is_not_run_when_the_backed_up_rows_changed_since_the_backup()
    {
        const string sql = "UPDATE Orders SET Status = 'cancelled' WHERE Id = 1";
        var backup = await BackupAsync(sql);
        Exec("UPDATE Orders SET Total = 99 WHERE Id = 1");

        var dry = await _service.DryRunAsync(_target, sql, backup);
        var ex = await Assert.ThrowsAsync<DatabaseToolException>(() => _service.ExecuteAsync(_target, sql, expectedRows: 1, backup));

        Assert.Contains("changed since", dry.Problem);
        Assert.Contains("changed since", ex.Message);
        Assert.Equal("1:new:99,2:new:20,3:paid:30.25", Rows());
    }

    // A trigger reaches a row outside the WHERE: restoring only the matched rows would leave it changed, so the dry
    // test stops the change.
    [Fact]
    public async Task A_change_that_reaches_rows_outside_the_backup_stops_in_the_dry_test()
    {
        Exec("CREATE TRIGGER touch AFTER UPDATE ON Orders WHEN NEW.Id = 1 BEGIN UPDATE Orders SET Status = 'touched' WHERE Id = 3; END;");
        const string sql = "UPDATE Orders SET Status = 'cancelled' WHERE Id = 1";
        var backup = await BackupAsync(sql);

        var dry = await _service.DryRunAsync(_target, sql, backup);

        Assert.Equal(1, backup.Rows);
        Assert.False(dry.RestoreVerified);
        Assert.Contains("outside the backup", dry.Problem);
        Assert.Equal("1:new:10.5,2:new:20,3:paid:30.25", Rows());
    }

    [Fact]
    public async Task A_damaged_backup_file_restores_nothing()
    {
        const string sql = "UPDATE Orders SET Status = 'cancelled' WHERE Status = 'new'";
        var backup = await BackupAsync(sql);
        await _service.ExecuteAsync(_target, sql, expectedRows: 2, backup);
        var bytes = await File.ReadAllBytesAsync(backup.Path);
        bytes[^10] ^= 0xFF;
        await File.WriteAllBytesAsync(backup.Path, bytes);

        await Assert.ThrowsAsync<DatabaseToolException>(() => _service.RestoreAsync(_target, backup));

        Assert.Equal("1:cancelled:10.5,2:cancelled:20,3:paid:30.25", Rows());
    }

    [Fact]
    public async Task A_backup_stops_when_the_disk_would_fill()
    {
        var options = new AIThreadOptions();
        options.Databases.Directory = _folder;
        options.Databases.BackupFreeSpaceReserveMb = int.MaxValue;
        var service = new DatabaseQueryService(Options.Create(options), NullLogger<DatabaseQueryService>.Instance);
        var check = SqlStatement.Classify("UPDATE Orders SET Status = 'x'", MemoryStoreProvider.Sqlite);

        var ex = await Assert.ThrowsAsync<DatabaseToolException>(() =>
            service.SnapshotAsync(_target, check.Target!, check.Scope, Path.Combine(_folder, "full.bak"), _protector));

        Assert.Contains("not enough disk space", ex.Message);
    }

    [Fact]
    public async Task A_table_without_primary_key_is_not_backed_up_for_a_change()
    {
        var ex = await Assert.ThrowsAsync<DatabaseToolException>(() => BackupAsync("DELETE FROM Logs WHERE Line = 'x'"));
        Assert.Contains("no primary key", ex.Message);
    }
}

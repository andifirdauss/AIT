using AIThread.Common.Enums;
using AIThread.Common.Options;
using AIThread.Data.Memory;
using AIThread.Service.Databases;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AIThread.Tests;

public class DatabaseQueryTests : IDisposable
{
    private readonly string _file = Path.Combine(Path.GetTempPath(), "aithread-tests", $"db-{Guid.NewGuid():N}.db");
    private readonly DatabaseTarget _target;
    private readonly DatabaseQueryService _service = new(Options.Create(new AIThreadOptions()), NullLogger<DatabaseQueryService>.Instance);

    public DatabaseQueryTests()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
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
        try { File.Delete(_file); } catch (IOException) { }
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
    public async Task Dry_run_changes_nothing_and_verifies_the_restore()
    {
        var backup = await _service.SnapshotAsync(_target, new TableName(null, "Orders"), forRestore: true);

        var dry = await _service.DryRunAsync(_target, "UPDATE Orders SET Status = 'cancelled' WHERE Status = 'new'", backup);

        Assert.Null(dry.Problem);
        Assert.Equal(2, dry.Affected);
        Assert.Equal(2, dry.ChangedRows);
        Assert.True(dry.RestoreVerified);
        Assert.Equal(2, dry.Restore.Updated);
        Assert.All(dry.Sample, c => Assert.Equal("updated", c.Kind));
        Assert.Equal("1:new:10.5,2:new:20,3:paid:30.25", Rows());
    }

    [Fact]
    public async Task Dry_run_restores_deleted_and_inserted_rows()
    {
        var backup = await _service.SnapshotAsync(_target, new TableName(null, "Orders"), forRestore: true);

        var deleted = await _service.DryRunAsync(_target, "DELETE FROM Orders WHERE Id = 2", backup);
        var inserted = await _service.DryRunAsync(_target, "INSERT INTO Orders (Id, Customer, Total, Status) VALUES (4, 'Dewi', 5, 'new')", backup);

        Assert.True(deleted.RestoreVerified);
        Assert.Equal(1, deleted.Restore.Inserted);
        Assert.True(inserted.RestoreVerified);
        Assert.Equal(1, inserted.Restore.Deleted);
        Assert.Equal("1:new:10.5,2:new:20,3:paid:30.25", Rows());
    }

    [Fact]
    public async Task Execute_commits_only_the_approved_row_count_and_restore_brings_the_rows_back()
    {
        var backup = await _service.SnapshotAsync(_target, new TableName(null, "Orders"), forRestore: true);
        const string sql = "UPDATE Orders SET Status = 'cancelled' WHERE Status = 'new'";

        await Assert.ThrowsAsync<DatabaseToolException>(() => _service.ExecuteAsync(_target, sql, expectedRows: 1));
        Assert.Equal("1:new:10.5,2:new:20,3:paid:30.25", Rows());

        Assert.Equal(2, await _service.ExecuteAsync(_target, sql, expectedRows: 2));
        Assert.Equal("1:cancelled:10.5,2:cancelled:20,3:paid:30.25", Rows());

        var counts = await _service.RestoreAsync(_target, backup);
        Assert.Equal(2, counts.Updated);
        Assert.Equal("1:new:10.5,2:new:20,3:paid:30.25", Rows());
    }

    [Fact]
    public async Task A_table_without_primary_key_is_not_backed_up_for_a_change()
    {
        var ex = await Assert.ThrowsAsync<DatabaseToolException>(() => _service.SnapshotAsync(_target, new TableName(null, "Logs"), forRestore: true));
        Assert.Contains("no primary key", ex.Message);
    }
}

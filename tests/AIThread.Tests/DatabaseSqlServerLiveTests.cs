using AIThread.Common.Enums;
using AIThread.Common.Options;
using AIThread.Data.Memory;
using AIThread.Service.Databases;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AIThread.Tests;

// The SQL Server specifics of the database tools against a real engine (LocalDB): the catalog primary key lookup,
// IDENTITY_INSERT on restore, a ROWVERSION column left out of restores and checksums, decimals and datetime2. Opt-in:
// AIT_LIVE_SQLSERVER=1 (server: AIT_LIVE_SQLSERVER_HOST, default (localdb)\MSSQLLocalDB). A throwaway database is
// created and dropped.
public class DatabaseSqlServerLiveTests
{
    private static bool Enabled => Environment.GetEnvironmentVariable("AIT_LIVE_SQLSERVER") == "1";

    private static string Host => Environment.GetEnvironmentVariable("AIT_LIVE_SQLSERVER_HOST") ?? @"(localdb)\MSSQLLocalDB";

    [Fact]
    public async Task Dry_test_restore_execute_and_restore_on_sql_server()
    {
        if (!Enabled) return;
        var name = $"AitDbTest_{Guid.NewGuid():N}"[..30];
        var master = $"Server={Host};Database=master;Integrated Security=True;TrustServerCertificate=True";
        await ExecAsync(master, $"CREATE DATABASE [{name}]");
        try
        {
            var connection = $"Server={Host};Database={name};Integrated Security=True;TrustServerCertificate=True";
            await ExecAsync(connection, """
                CREATE TABLE dbo.Orders (
                    Id INT IDENTITY(1,1) PRIMARY KEY,
                    Email NVARCHAR(200) NULL,
                    Total DECIMAL(10,2) NOT NULL,
                    Status NVARCHAR(20) NOT NULL,
                    Created DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
                    RowVer ROWVERSION);
                INSERT INTO dbo.Orders (Email, Total, Status) VALUES ('ana@example.com', 10.50, 'new'), ('budi@example.com', 20.00, 'new'), (NULL, 30.25, 'paid');
                """);

            var service = new DatabaseQueryService(Options.Create(new AIThreadOptions()), NullLogger<DatabaseQueryService>.Instance);
            var target = new DatabaseTarget(Guid.NewGuid(), Guid.NewGuid(), "shop", new MemoryStoreConnection(MemoryStoreProvider.SqlServer, Host, null, name), DatabaseEnvironment.Dev,
                [], 50, true, null);
            var table = SqlStatement.Classify("UPDATE [dbo].[Orders] SET Status = 'x'", MemoryStoreProvider.SqlServer).Target!;

            var description = await service.DescribeTableAsync(target, table);
            Assert.Equal(["Id"], description.PrimaryKey);
            Assert.Contains(description.Columns, c => c.Name == "Id" && c.IsIdentity && c.Writable);
            Assert.Contains(description.Columns, c => c.Name == "RowVer" && !c.Writable);

            var read = await service.QueryAsync(target, "SELECT TOP 2 Id, Total FROM dbo.Orders ORDER BY Id", 10);
            Assert.Equal(2, read.Rows.Count);
            await Assert.ThrowsAsync<DatabaseToolException>(() => service.QueryAsync(target, "DELETE FROM dbo.Orders", 10));

            var backup = await service.SnapshotAsync(target, table, forRestore: true);
            var deleted = await service.DryRunAsync(target, "DELETE FROM dbo.Orders WHERE Status = 'new'", backup);
            Assert.Null(deleted.Problem);
            Assert.Equal(2, deleted.Affected);
            Assert.True(deleted.RestoreVerified);
            Assert.Equal(2, deleted.Restore.Inserted);

            var updated = await service.DryRunAsync(target, "UPDATE o SET Total = o.Total * 2 FROM dbo.Orders o WHERE o.Id = 1", backup);
            Assert.True(updated.RestoreVerified);
            Assert.Equal(1, updated.Affected);

            Assert.Equal(1, await service.ExecuteAsync(target, "UPDATE dbo.Orders SET Status = 'cancelled' WHERE Id = 2", 1));
            Assert.Equal(1, (await service.RestoreAsync(target, backup)).Updated);
            var after = await service.QueryAsync(target, "SELECT Status FROM dbo.Orders WHERE Id = 2", 1);
            Assert.Equal("new", after.Rows[0][0].Text);
        }
        finally
        {
            SqlConnection.ClearAllPools();
            await ExecAsync(master, $"ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}]");
        }
    }

    private static async Task ExecAsync(string connectionString, string sql)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
}

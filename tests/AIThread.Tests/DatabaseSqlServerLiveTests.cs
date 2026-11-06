using AIThread.Common.Enums;
using AIThread.Common.Options;
using AIThread.Data.Memory;
using AIThread.Service.Databases;
using Microsoft.AspNetCore.DataProtection;
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
        var folder = Path.Combine(Path.GetTempPath(), "aithread-tests", $"live-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
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

            var options = new AIThreadOptions();
            options.Databases.Directory = folder;
            var service = new DatabaseQueryService(Options.Create(options), NullLogger<DatabaseQueryService>.Instance);
            var protector = new EphemeralDataProtectionProvider().CreateProtector("live");
            var target = new DatabaseTarget(Guid.NewGuid(), Guid.NewGuid(), "shop", new MemoryStoreConnection(MemoryStoreProvider.SqlServer, Host, null, name), DatabaseEnvironment.Dev,
                [], 50, true, null);
            var table = SqlStatement.Classify("UPDATE [dbo].[Orders] SET Status = 'x'", MemoryStoreProvider.SqlServer).Target!;

            Task<BackupSnapshot> BackupAsync(string sql)
            {
                var check = SqlStatement.Classify(sql, MemoryStoreProvider.SqlServer);
                return service.SnapshotAsync(target, check.Target!, check.Scope, Path.Combine(folder, $"{Guid.NewGuid():N}.bak"), protector);
            }

            var description = await service.DescribeTableAsync(target, table);
            Assert.Equal(["Id"], description.PrimaryKey);
            Assert.Contains(description.Columns, c => c.Name == "Id" && c.IsIdentity && c.Writable);
            Assert.Contains(description.Columns, c => c.Name == "RowVer" && !c.Writable);

            var read = await service.QueryAsync(target, "SELECT TOP 2 Id, Total FROM dbo.Orders ORDER BY Id", 10);
            Assert.Equal(2, read.Rows.Count);
            await Assert.ThrowsAsync<DatabaseToolException>(() => service.QueryAsync(target, "DELETE FROM dbo.Orders", 10));

            // A WHERE backup: the deleted rows come back with their identity values (IDENTITY_INSERT).
            const string delete = "DELETE FROM dbo.Orders WHERE Status = 'new'";
            var deleteBackup = await BackupAsync(delete);
            Assert.Equal(BackupScopeKind.Rows, deleteBackup.Header.Scope.Kind);
            var deleted = await service.DryRunAsync(target, delete, deleteBackup);
            Assert.Null(deleted.Problem);
            Assert.Equal(2, deleted.Affected);
            Assert.True(deleted.RestoreVerified);
            Assert.Equal(2, deleted.Restore.Inserted);

            // UPDATE … FROM: the whole table.
            const string update = "UPDATE o SET Total = o.Total * 2 FROM dbo.Orders o WHERE o.Id = 1";
            var updateBackup = await BackupAsync(update);
            Assert.Equal(BackupScopeKind.Table, updateBackup.Header.Scope.Kind);
            var updated = await service.DryRunAsync(target, update, updateBackup);
            Assert.True(updated.RestoreVerified, updated.Problem);
            Assert.Equal(1, updated.Affected);

            const string cancel = "UPDATE dbo.Orders SET Status = 'cancelled' WHERE Id = 2";
            var cancelBackup = await BackupAsync(cancel);
            Assert.Equal(1, await service.ExecuteAsync(target, cancel, 1, cancelBackup));
            Assert.Equal(1, (await service.RestoreAsync(target, cancelBackup)).Updated);
            var after = await service.QueryAsync(target, "SELECT Status FROM dbo.Orders WHERE Id = 2", 1);
            Assert.Equal("new", after.Rows[0][0].Text);

            // An INSERT into an IDENTITY table: the dry test's identity value is not the one the real run gets, so the
            // keys are recorded when it runs; the restore deletes exactly that row.
            const string insert = "INSERT INTO dbo.Orders (Email, Total, Status) VALUES (NULL, 5, 'new')";
            var insertBackup = await BackupAsync(insert);
            var inserted = await service.DryRunAsync(target, insert, insertBackup);
            Assert.True(inserted.RestoreVerified, inserted.Problem);
            Assert.Equal(1, inserted.ChangedRows);
            Assert.Equal(1, await service.ExecuteAsync(target, insert, 1, insertBackup));
            Assert.Equal(1, (await service.RestoreAsync(target, insertBackup)).Deleted);
            var count = await service.QueryAsync(target, "SELECT COUNT(*) FROM dbo.Orders", 1);
            Assert.Equal("3", count.Rows[0][0].Text);
        }
        finally
        {
            SqlConnection.ClearAllPools();
            try { Directory.Delete(folder, recursive: true); } catch (IOException) { }
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

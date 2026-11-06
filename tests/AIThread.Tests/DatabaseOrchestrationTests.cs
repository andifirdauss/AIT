using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using AIThread.Common.DTOs;
using AIThread.Common.Enums;
using AIThread.Service.Interfaces;
using AIThread.Service.Orchestration;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using static AIThread.Tests.ThreadOrchestratorTests;

namespace AIThread.Tests;

// change_database end to end: backup → dry test + restore test → the developer's card → the real change; Prod offers a
// local rehearsal first. The project database is a SQLite file, the planner is scripted.
public partial class DatabaseOrchestrationTests : IDisposable
{
    private readonly string _file = Path.Combine(Path.GetTempPath(), "aithread-tests", $"shop-{Guid.NewGuid():N}.db");

    public DatabaseOrchestrationTests()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
        Exec("""
            CREATE TABLE Orders (Id INTEGER PRIMARY KEY, Email TEXT, Status TEXT NOT NULL);
            INSERT INTO Orders VALUES (1, 'ana@example.com', 'new');
            INSERT INTO Orders VALUES (2, 'budi@example.com', 'new');
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

    private string Statuses()
    {
        using var connection = new SqliteConnection($"Data Source={_file}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT group_concat(Id || ':' || Status, ',') FROM (SELECT * FROM Orders ORDER BY Id)";
        return (string)command.ExecuteScalar()!;
    }

    private async Task AddDatabaseAsync(OrchestrationHarness harness, DatabaseEnvironment environment) =>
        await harness.Service<IProjectDatabaseService>().CreateAsync(harness.ProjectId,
            new SaveProjectDatabaseRequest("shop", MemoryStoreProvider.Sqlite, null, null, _file, null, null, null, environment, null, 50, true, "orders of the shop"));

    private static string LastToolResult(ScriptedRouter router, int call) =>
        router.CallsTo(ScriptedAgent.Planner)[call].Messages.OfType<JsonObject>().Last(m => m["role"]?.GetValue<string>() == "tool")["content"]!.GetValue<string>();

    [Fact]
    public async Task A_dev_change_is_backed_up_dry_tested_and_runs_after_permission()
    {
        await using var harness = await CreateAsync();
        await AddDatabaseAsync(harness, DatabaseEnvironment.Dev);
        var router = harness.Router;
        router
            .On(ScriptedAgent.Planner, router.Tool(PlannerTools.ChangeDatabase, new { database = "shop", sql = "UPDATE Orders SET Status = 'cancelled' WHERE Id = 1", reason = "batalkan order 1" }))
            .On(ScriptedAgent.Planner, router.SubmitPlan(new ThreadPlan { Summary = "Diubah.", Answer = "Order 1 dibatalkan." }));

        var (first, firstObserver) = await harness.RunAsync("Batalkan order 1", ThreadSource.ClaudeCode, "session-db");

        Assert.True(first.Status == ThreadStatus.AwaitingInput, firstObserver.Final);
        Assert.Contains("Dry test (rolled back): 1 row(s) affected", firstObserver.Final);
        Assert.Contains("Restore test: passed", firstObserver.Final);
        Assert.Contains("Izinkan sekali", firstObserver.Final);
        Assert.DoesNotContain("Latihan lokal", firstObserver.Final);
        Assert.Equal("1:new,2:new", Statuses());

        var (second, _) = await harness.RunAsync("1", ThreadSource.ClaudeCode, "session-db");

        Assert.Equal(ThreadStatus.Completed, second.Status);
        Assert.Equal("1:cancelled,2:new", Statuses());
        Assert.Contains("Committed: 1 row(s)", LastToolResult(router, 1));
        var backup = Assert.Single(await harness.Service<IProjectDatabaseService>().GetBackupsAsync(harness.ProjectId));
        Assert.Equal(2, backup.Rows);
        Assert.Equal(1, backup.AffectedRows);
        Assert.True(backup.FileExists);

        var restored = await harness.Service<IProjectDatabaseService>().RestoreBackupAsync(harness.ProjectId, backup.Id);
        Assert.True(restored.Success);
        Assert.Equal("1:new,2:new", Statuses());
    }

    [Fact]
    public async Task A_prod_change_offers_a_local_rehearsal_and_asks_again_before_running()
    {
        await using var harness = await CreateAsync();
        await AddDatabaseAsync(harness, DatabaseEnvironment.Prod);
        var router = harness.Router;
        var change = new { database = "shop", sql = "UPDATE Orders SET Status = 'paid' WHERE Status = 'new'", sqlite_sql = "UPDATE Orders SET Status = 'paid' WHERE Status = 'new'", reason = "tandai lunas" };
        router
            .On(ScriptedAgent.Planner, router.Tool(PlannerTools.ChangeDatabase, change))
            .On(ScriptedAgent.Planner, router.Tool(PlannerTools.ChangeDatabase, change))
            .On(ScriptedAgent.Planner, router.SubmitPlan(new ThreadPlan { Summary = "Diubah.", Answer = "Semua order lunas." }));

        var (first, firstObserver) = await harness.RunAsync("Tandai semua order baru lunas", ThreadSource.ClaudeCode, "session-prod");
        Assert.True(first.Status == ThreadStatus.AwaitingInput, firstObserver.Final);
        Assert.Contains("Latihan lokal dulu", firstObserver.Final);
        Assert.Contains("PROD", firstObserver.Final);
        Assert.DoesNotContain("Izinkan selalu", firstObserver.Final);

        var (second, secondObserver) = await harness.RunAsync("6", ThreadSource.ClaudeCode, "session-prod");
        Assert.Equal(ThreadStatus.AwaitingInput, second.Status);
        Assert.Contains("Rehearsal on a local SQLite copy", LastToolResult(router, 1));
        Assert.Contains("2 row(s) affected", LastToolResult(router, 1));
        Assert.Contains("Hasil latihan lokal", secondObserver.Final);
        Assert.DoesNotContain("6. **Latihan lokal dulu**", secondObserver.Final);
        Assert.Equal("1:new,2:new", Statuses());
        Assert.Empty(Directory.GetFiles(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AIThread", "db-rehearsal"), $"{first.ThreadId:N}.db"));

        var (third, _) = await harness.RunAsync("1", ThreadSource.ClaudeCode, "session-prod");
        Assert.Equal(ThreadStatus.Completed, third.Status);
        Assert.Equal("1:paid,2:paid", Statuses());
    }

    // A table above BackupMaxRows is refused as a whole: no partial backup, no dry test, nothing executed.
    [Fact]
    public async Task A_table_above_the_backup_limit_is_refused_and_left_untouched()
    {
        Exec("INSERT INTO Orders VALUES (3, 'citra@example.com', 'new');");
        await using var harness = await OrchestrationHarness.CreateAsync(configureServices: services =>
            services.Configure<AIThread.Common.Options.AIThreadOptions>(o => o.Databases.BackupMaxRows = 2));
        await harness.AddStandardNodesAsync();
        await AddDatabaseAsync(harness, DatabaseEnvironment.Dev);
        var router = harness.Router;
        router
            .On(ScriptedAgent.Planner, router.Tool(PlannerTools.ChangeDatabase, new { database = "shop", sql = "UPDATE Orders SET Status = 'x' WHERE Id = 1", reason = "ubah satu order" }))
            .On(ScriptedAgent.Planner, router.SubmitPlan(new ThreadPlan { Summary = "Tidak bisa.", Answer = "Tabel terlalu besar untuk di-backup AIT." }));

        var (result, observer) = await harness.RunAsync("Ubah order 1", ThreadSource.ClaudeCode, "session-big");

        Assert.Equal(ThreadStatus.Completed, result.Status);
        Assert.Contains("the backup failed, so nothing was run", LastToolResult(router, 1));
        Assert.Contains("has 3 rows, more than the 2", LastToolResult(router, 1));
        Assert.DoesNotContain("Izinkan sekali", observer.Final);
        Assert.Equal("1:new,2:new,3:new", Statuses());
        Assert.Empty(await harness.Service<IProjectDatabaseService>().GetBackupsAsync(harness.ProjectId));
    }

    [Fact]
    public async Task A_table_without_primary_key_is_refused_before_any_card()
    {
        await using var harness = await CreateAsync();
        await AddDatabaseAsync(harness, DatabaseEnvironment.Dev);
        var router = harness.Router;
        router
            .On(ScriptedAgent.Planner, router.Tool(PlannerTools.ChangeDatabase, new { database = "shop", sql = "DELETE FROM Logs", reason = "bersihkan log" }))
            .On(ScriptedAgent.Planner, router.SubmitPlan(new ThreadPlan { Summary = "Tidak bisa.", Answer = "Tabel Logs tidak punya primary key." }));

        var (result, _) = await harness.RunAsync("Hapus semua log", ThreadSource.ClaudeCode, "session-logs");

        Assert.Equal(ThreadStatus.Completed, result.Status);
        Assert.Contains("the backup failed, so nothing was run", LastToolResult(router, 1));
        Assert.Contains("no primary key", LastToolResult(router, 1));
        Assert.Empty(await harness.Service<IProjectDatabaseService>().GetBackupsAsync(harness.ProjectId));
    }

    [Fact]
    public async Task Sensitive_values_reach_the_model_as_placeholders_and_come_back_in_a_change()
    {
        await using var harness = await CreateAsync();
        await AddDatabaseAsync(harness, DatabaseEnvironment.Dev);
        var router = harness.Router;
        router.On(ScriptedAgent.Planner, router.Tool(PlannerTools.QueryDatabase, new { database = "shop", sql = "SELECT Id, Email FROM Orders WHERE Id = 2" }));
        Assert.Equal(ThreadStatus.Completed, await RunUntilQueryAsync(harness));
        var rows = LastToolResult(router, 1);
        Assert.DoesNotContain("budi@example.com", rows);
        var placeholder = PlaceholderPattern().Match(rows).Value;
        Assert.StartsWith("{{AIT_SECRET:pii-Email:", placeholder);

        router
            .On(ScriptedAgent.Planner, router.Tool(PlannerTools.ChangeDatabase, new { database = "shop", sql = $"UPDATE Orders SET Status = 'vip' WHERE Email = '{placeholder}'", reason = "tandai vip" }))
            .On(ScriptedAgent.Planner, router.SubmitPlan(new ThreadPlan { Summary = "Diubah.", Answer = "Order budi jadi vip." }));
        var (paused, observer) = await harness.RunAsync("lanjut", ThreadSource.ClaudeCode, "session-pii");
        Assert.Equal(ThreadStatus.AwaitingInput, paused.Status);
        Assert.DoesNotContain("budi@example.com", observer.Final);
        Assert.Contains(placeholder, observer.Final);

        await harness.RunAsync("1", ThreadSource.ClaudeCode, "session-pii");
        Assert.Equal("1:new,2:vip", Statuses());
    }

    // The query call, then a plan that answers, so the thread ends and the next prompt starts a new one.
    private static async Task<ThreadStatus> RunUntilQueryAsync(OrchestrationHarness harness)
    {
        harness.Router.On(ScriptedAgent.Planner, harness.Router.SubmitPlan(new ThreadPlan { Summary = "Dibaca.", Answer = "Email order 2 sudah dibaca." }));
        var (result, _) = await harness.RunAsync("Baca email order 2", ThreadSource.ClaudeCode, "session-pii");
        return result.Status;
    }

    [GeneratedRegex(@"\{\{AIT_SECRET:pii-Email:[0-9a-f]{10}\}\}")]
    private static partial Regex PlaceholderPattern();
}

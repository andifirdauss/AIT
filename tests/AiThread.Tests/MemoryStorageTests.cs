using AiThread.Common.Entities;
using AiThread.Common.Enums;
using AiThread.Data.Cache;
using AiThread.Data.Context;
using AiThread.Data.Memory;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace AiThread.Tests;

public class MemoryStorageTests
{
    [Fact]
    public void PostgreSql_connection_string_carries_server_and_credentials()
    {
        var connection = new MemoryStoreConnection(MemoryStoreProvider.PostgreSql, "db.local", null, "aithread", "app", "s3cret");

        var cs = connection.BuildConnectionString();

        Assert.Contains("Host=db.local", cs);
        Assert.Contains("Port=5432", cs);
        Assert.Contains("Database=aithread", cs);
        Assert.Contains("Username=app", cs);
        Assert.Contains("Password=s3cret", cs);
    }

    [Fact]
    public void SqlServer_without_user_uses_windows_authentication()
    {
        var cs = new MemoryStoreConnection(MemoryStoreProvider.SqlServer, "sql01", 1444, "Memory").BuildConnectionString();

        Assert.Contains("Data Source=sql01,1444", cs);
        Assert.Contains("Integrated Security=True", cs);
        Assert.DoesNotContain("User ID", cs);
    }

    [Fact]
    public void Oracle_uses_easy_connect_with_service_name()
    {
        var cs = new MemoryStoreConnection(MemoryStoreProvider.Oracle, "ora.local", null, "FREEPDB1", "hr", "pw").BuildConnectionString();

        Assert.Contains("ora.local:1521/FREEPDB1", cs);
        Assert.Contains("hr", cs);
    }

    [Fact]
    public void Options_override_the_fields()
    {
        var cs = new MemoryStoreConnection(MemoryStoreProvider.PostgreSql, "db", 5432, "x", "u", "p", "Timeout=30;SSL Mode=Require")
            .BuildConnectionString();

        Assert.Contains("Timeout=30", cs);
        Assert.Contains("SSL Mode=Require", cs);
    }

    [Fact]
    public void Unknown_option_is_rejected()
    {
        var connection = new MemoryStoreConnection(MemoryStoreProvider.PostgreSql, "db", null, "x", "u", "p", "NoSuchKeyword=1");

        Assert.ThrowsAny<ArgumentException>(() => connection.BuildConnectionString());
    }

    [Fact]
    public void Redis_configuration_carries_endpoint_password_and_database()
    {
        var configuration = new MemoryStoreConnection(MemoryStoreProvider.Redis, "cache.local", 6380, "2", null, "pw", "ssl=true")
            .BuildRedisConfiguration();

        Assert.Contains("cache.local:6380", configuration);
        Assert.Contains("password=pw", configuration);
        Assert.Contains("defaultDatabase=2", configuration);
        Assert.Contains("ssl=true", configuration);
    }

    [Fact]
    public void Description_never_contains_the_password()
    {
        var connection = new MemoryStoreConnection(MemoryStoreProvider.MySql, "my.local", 3307, "mem", "root", "topsecret");

        Assert.Equal("MySQL · my.local:3307/mem", connection.Describe());
        Assert.DoesNotContain("topsecret", connection.Describe());
    }

    [Fact]
    public async Task Sql_short_memory_store_creates_its_table_and_round_trips()
    {
        using var workspace = new TempWorkspace();
        var store = new SqlShortMemoryStore(Sqlite(workspace, "nested/short.db"), NullLogger.Instance);
        var old = new ShortMemoryRecord(Guid.NewGuid(), Guid.NewGuid(), MemoryType.Project, "old", "two days ago", null, DateTime.UtcNow.AddDays(-2));
        var fresh = new ShortMemoryRecord(Guid.NewGuid(), null, MemoryType.Knowledge, "fresh", "now", Guid.NewGuid(), DateTime.UtcNow);

        Assert.True(await store.ProbeAsync());
        await store.SaveAsync(old);
        await store.SaveAsync(fresh);
        await store.SaveAsync(fresh with { Label = "fresh, edited" });

        var all = await store.GetAllAsync();
        Assert.Equal(2, all.Count);
        Assert.Equal("fresh, edited", all[0].Label);
        Assert.Equal(DateTimeKind.Utc, all[0].CreatedAt.Kind);

        var due = await store.GetCreatedBeforeAsync(DateTime.UtcNow.AddDays(-1));
        Assert.Equal(old.Id, Assert.Single(due).Id);

        Assert.True(await store.DeleteAsync(old.Id));
        Assert.Null(await store.GetAsync(old.Id));
        Assert.True(store.IsConnected);
    }

    [Fact]
    public async Task Short_and_long_memory_can_share_one_database()
    {
        using var workspace = new TempWorkspace();
        var connection = Sqlite(workspace, "memory.db");

        var shortStore = new SqlShortMemoryStore(connection, NullLogger.Instance);
        await shortStore.SaveAsync(new ShortMemoryRecord(Guid.NewGuid(), null, MemoryType.Knowledge, "s", "short", null, DateTime.UtcNow));

        // The ShortMemories table exists already; the long store must still create LongMemories.
        var longStore = new LongMemoryStore(new UnusedMainDatabase());
        longStore.Configure(connection);
        Assert.True(longStore.IsExternal);

        var id = Guid.NewGuid();
        await using (var db = await longStore.CreateAsync())
        {
            db.LongMemories.Add(new LongMemory { Id = id, Type = MemoryType.Knowledge, Label = "l", Description = "long" });
            await db.SaveChangesAsync();
        }

        await using (var db = await longStore.CreateAsync())
        {
            var row = await db.LongMemories.AsNoTracking().SingleAsync();
            Assert.Equal(id, row.Id);
            Assert.Equal(DateTimeKind.Utc, row.CreatedAt.Kind);
        }

        Assert.Single(await shortStore.GetAllAsync());
    }

    [Fact]
    public async Task Testing_a_new_sqlite_store_reports_that_the_table_will_be_created()
    {
        using var workspace = new TempWorkspace();

        var result = await MemoryStoreTester.TestAsync(MemoryLevel.Long, Sqlite(workspace, "new.db"));

        Assert.True(result.Success, result.Message);
        Assert.Contains("akan dibuat", result.Message);
        Assert.False(File.Exists(Path.Combine(workspace.Root, "new.db")));
    }

    [Fact]
    public void Switching_back_to_default_keeps_the_built_in_store()
    {
        using var redis = new RedisConnectionProvider("127.0.0.1:1", NullLogger<RedisConnectionProvider>.Instance);
        using var store = new SwitchableShortMemoryStore(redis, "127.0.0.1:1", NullLoggerFactory.Instance);
        using var workspace = new TempWorkspace();

        store.Configure(Sqlite(workspace, "short.db"));
        Assert.Equal(MemoryStoreProvider.Sqlite, store.Connection.Provider);
        Assert.StartsWith("SQLite", store.Description);

        store.Configure(MemoryStoreConnection.Default);
        Assert.Equal(MemoryStoreProvider.Default, store.Connection.Provider);
        Assert.StartsWith("Garnet bawaan", store.Description);
    }

    // Pooling off so the temp folder can be deleted afterwards.
    private static MemoryStoreConnection Sqlite(TempWorkspace workspace, string file) =>
        new(MemoryStoreProvider.Sqlite, Database: Path.Combine(workspace.Root, file), Options: "Pooling=False");

    private sealed class UnusedMainDatabase : IDbContextFactory<AiThreadDbContext>
    {
        public AiThreadDbContext CreateDbContext() => throw new InvalidOperationException("The main database must not be used.");
    }
}

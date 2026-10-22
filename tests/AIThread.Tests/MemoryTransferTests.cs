using AIThread.Common.DTOs;
using AIThread.Common.Entities;
using AIThread.Common.Enums;
using AIThread.Data.Cache;
using AIThread.Data.Context;
using AIThread.Data.Memory;
using AIThread.Data.Messaging;
using AIThread.Service.Implementations;
using AIThread.Service.Interfaces;
using AIThread.Service.Memory;
using AIThread.Service.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace AIThread.Tests;

// Memory exported on one machine lands on the same project (by key) on another, and importing is idempotent.
public class MemoryTransferTests
{
    private const string Key = "github.com/org/eep";

    [Fact]
    public async Task Export_then_import_on_another_machine_follows_the_project_key()
    {
        MemoryExportFile file;
        Guid shortId, longId, knowledgeId;
        DateTime shortCreated;
        await using (var source = await OrchestrationHarness.CreateAsync())
        {
            using var shortDb = new TempWorkspace();
            using var transfer = TransferFixture.Create(source, shortDb);
            await SetKeyAsync(source, source.ProjectId, Key);
            shortCreated = DateTime.UtcNow.AddHours(-2);
            shortId = await transfer.AddShortAsync(new ShortMemoryRecord(Guid.NewGuid(), source.ProjectId, MemoryType.Project, "transfer order, approval",
                "TO above 10M needs approval.", null, shortCreated, Key, "EEP"));
            longId = await AddLongAsync(source, new LongMemory { Id = Guid.NewGuid(), ProjectId = source.ProjectId, Type = MemoryType.Project, Label = "legacy", Description = "row without key", HitCount = 4 });
            knowledgeId = await AddLongAsync(source, new LongMemory { Id = Guid.NewGuid(), Type = MemoryType.Knowledge, Label = "cache", Description = "Cache master data." });

            file = await transfer.Service.ExportAsync(new MemoryQuery(null, null, null, null));
        }

        Assert.Equal((MemoryExportFile.FormatName, 3), (file.Format, file.Items.Count));
        // A row written before keys existed is named by the key its project has on the exporting machine.
        Assert.Equal(Key, file.Items.Single(i => i.Id == longId).ProjectKey);

        await using var target = await OrchestrationHarness.CreateAsync();
        using var targetShort = new TempWorkspace();
        using var targetTransfer = TransferFixture.Create(target, targetShort);
        await SetKeyAsync(target, target.ProjectId, Key);

        var preview = await targetTransfer.Service.ImportAsync(file, MemoryImportMode.Skip, dryRun: true);
        Assert.Equal((3, 0), (preview.Added, preview.Skipped));
        Assert.True(Assert.Single(preview.Projects).Registered);
        Assert.Empty(await targetTransfer.ShortAsync());

        var result = await targetTransfer.Service.ImportAsync(file, MemoryImportMode.Skip, dryRun: false);
        Assert.Equal(3, result.Added);

        var imported = Assert.Single(await targetTransfer.ShortAsync());
        Assert.Equal((shortId, target.ProjectId, Key), (imported.Id, imported.ProjectId, imported.ProjectKey));
        Assert.Equal(shortCreated, imported.CreatedAt, TimeSpan.FromSeconds(1));

        await using var db = await target.CreateDbAsync();
        var legacy = await db.LongMemories.AsNoTracking().SingleAsync(m => m.Id == longId);
        Assert.Equal((target.ProjectId, Key, 4), (legacy.ProjectId, legacy.ProjectKey, legacy.HitCount));
        var knowledge = await db.LongMemories.AsNoTracking().SingleAsync(m => m.Id == knowledgeId);
        Assert.Equal((MemoryType.Knowledge, (Guid?)null, (string?)null), (knowledge.Type, knowledge.ProjectId, knowledge.ProjectKey));
    }

    [Fact]
    public async Task Importing_again_or_the_same_content_under_another_id_never_duplicates()
    {
        await using var harness = await OrchestrationHarness.CreateAsync();
        using var shortDb = new TempWorkspace();
        using var transfer = TransferFixture.Create(harness, shortDb);
        var item = Item(Guid.NewGuid(), "invoice, tax", "Tax is rounded up per line.");
        var file = File(item, item with { Id = Guid.NewGuid(), Label = "Invoice,  TAX", Description = "tax is rounded up  per line." });

        var first = await transfer.Service.ImportAsync(file, MemoryImportMode.Skip, dryRun: false);
        Assert.Equal((1, 1), (first.Added, first.Duplicates));

        var second = await transfer.Service.ImportAsync(file, MemoryImportMode.Skip, dryRun: false);
        Assert.Equal((0, 1, 1), (second.Added, second.Skipped, second.Duplicates));
        Assert.Single(await transfer.ShortAsync());
    }

    [Fact]
    public async Task Overwrite_mode_replaces_the_content_of_an_existing_id()
    {
        await using var harness = await OrchestrationHarness.CreateAsync();
        using var shortDb = new TempWorkspace();
        using var transfer = TransferFixture.Create(harness, shortDb);
        var id = await AddLongAsync(harness, new LongMemory { Id = Guid.NewGuid(), Type = MemoryType.Knowledge, Label = "old", Description = "old text" });
        var file = File(Item(id, "new", "new text", MemoryLevel.Long));

        Assert.Equal(1, (await transfer.Service.ImportAsync(file, MemoryImportMode.Skip, dryRun: false)).Skipped);
        Assert.Equal(1, (await transfer.Service.ImportAsync(file, MemoryImportMode.Overwrite, dryRun: false)).Updated);

        await using var db = await harness.CreateDbAsync();
        var row = await db.LongMemories.AsNoTracking().SingleAsync(m => m.Id == id);
        Assert.Equal(("new", "new text"), (row.Label, row.Description));
    }

    [Fact]
    public async Task Invalid_items_are_reported_and_keyless_project_memory_becomes_knowledge()
    {
        await using var harness = await OrchestrationHarness.CreateAsync();
        using var shortDb = new TempWorkspace();
        using var transfer = TransferFixture.Create(harness, shortDb);
        var orphan = Item(Guid.NewGuid(), "orphan", "From a project nobody registered.") with { Type = MemoryType.Project, ProjectId = Guid.NewGuid() };
        var foreign = Item(Guid.NewGuid(), "foreign", "Machine-only key.") with { Type = MemoryType.Project, ProjectKey = "local:abc" };
        var file = File(orphan, foreign, Item(Guid.NewGuid(), " ", "no label"), Item(Guid.NewGuid(), "biz", "wiki", MemoryLevel.Business));

        var result = await transfer.Service.ImportAsync(file, MemoryImportMode.Skip, dryRun: false);

        Assert.Equal((2, 2), (result.Added, result.Invalid));
        Assert.Contains(result.Warnings, w => w.Contains("dijadikan knowledge"));
        Assert.Contains(result.Warnings, w => w.Contains("key lokal"));
        Assert.False(Assert.Single(result.Projects).Registered);
        var stored = (await transfer.ShortAsync()).ToDictionary(r => r.Id);
        Assert.Equal(MemoryType.Knowledge, stored[orphan.Id].Type);
        Assert.Equal(("local:abc", MemoryType.Project), (stored[foreign.Id].ProjectKey, stored[foreign.Id].Type));

        await Assert.ThrowsAsync<AppValidationException>(() =>
            transfer.Service.ImportAsync(file with { Format = "something-else" }, MemoryImportMode.Skip, dryRun: true));
    }

    private static MemoryExportItem Item(Guid id, string label, string description, MemoryLevel level = MemoryLevel.Short) =>
        new(id, level, MemoryType.Knowledge, null, null, null, label, description, null, DateTime.UtcNow.AddMinutes(-5));

    private static MemoryExportFile File(params MemoryExportItem[] items) =>
        new(MemoryExportFile.FormatName, MemoryExportFile.CurrentVersion, DateTime.UtcNow, "test", items);

    private static async Task SetKeyAsync(OrchestrationHarness harness, Guid projectId, string key)
    {
        await using var db = await harness.CreateDbAsync();
        await db.Projects.Where(p => p.Id == projectId).ExecuteUpdateAsync(s => s.SetProperty(p => p.ProjectKey, key));
    }

    private static async Task<Guid> AddLongAsync(OrchestrationHarness harness, LongMemory row)
    {
        await using var db = await harness.CreateDbAsync();
        db.LongMemories.Add(row);
        await db.SaveChangesAsync();
        return row.Id;
    }

    // The real MemoryTransferService over the harness's main database, with short memory in a SQLite file.
    private sealed class TransferFixture : IDisposable
    {
        private readonly RedisConnectionProvider _redis;
        private readonly SwitchableShortMemoryStore _short;

        private TransferFixture(RedisConnectionProvider redis, SwitchableShortMemoryStore shortStore, MemoryTransferService service)
        {
            _redis = redis;
            _short = shortStore;
            Service = service;
        }

        public MemoryTransferService Service { get; }

        public static TransferFixture Create(OrchestrationHarness harness, TempWorkspace shortDb)
        {
            var redis = new RedisConnectionProvider("127.0.0.1:1", NullLogger<RedisConnectionProvider>.Instance);
            var shortStore = new SwitchableShortMemoryStore(redis, "127.0.0.1:1", NullLoggerFactory.Instance);
            shortStore.Configure(new MemoryStoreConnection(MemoryStoreProvider.Sqlite, Database: Path.Combine(shortDb.Root, "short.db"), Options: "Pooling=False"));

            var service = new MemoryTransferService(
                harness.Service<IDbContextFactory<AIThreadDbContext>>(),
                shortStore,
                harness.Service<LongMemoryStore>(),
                harness.Service<IModuleRegistry>(),
                harness.Service<IEventBus>(),
                harness.Service<IActivityLogService>(),
                harness.Service<ISecretGuard>(),
                NullLogger<MemoryTransferService>.Instance);
            return new TransferFixture(redis, shortStore, service);
        }

        public async Task<Guid> AddShortAsync(ShortMemoryRecord record)
        {
            await _short.SaveAsync(record);
            return record.Id;
        }

        public Task<IReadOnlyList<ShortMemoryRecord>> ShortAsync() => _short.GetAllAsync();

        public void Dispose()
        {
            _short.Dispose();
            _redis.Dispose();
        }
    }
}

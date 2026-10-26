using AIThread.Common.DTOs;
using AIThread.Common.Entities;
using AIThread.Common.Enums;
using AIThread.Common.Options;
using AIThread.Data.Cache;
using AIThread.Data.Context;
using AIThread.Data.Memory;
using AIThread.Data.Messaging;
using AIThread.Service.Business;
using AIThread.Service.Implementations;
using AIThread.Service.Interfaces;
using AIThread.Service.Memory;
using AIThread.Service.Workspace;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AIThread.Tests;

// Memory is shared by every clone of a repository through Project.ProjectKey, whatever the root folder.
public class ProjectKeyMemoryTests
{
    [Fact]
    public async Task Clones_with_the_same_key_share_project_memory_and_others_do_not()
    {
        await using var harness = await OrchestrationHarness.CreateAsync();
        using var shortDb = new TempWorkspace();
        using var memory = await MemoryFixture.CreateAsync(harness, shortDb);
        var cloneA = harness.ProjectId;
        var cloneB = await AddProjectAsync(harness, "EEP (clone B)", "github.com/org/eep");
        var other = await AddProjectAsync(harness, "Other", "github.com/org/other");
        await SetKeyAsync(harness, cloneA, "github.com/org/eep");

        var saved = await memory.Service.AddShortAsync(new SaveMemoryRequest(MemoryType.Project, cloneA, "TransferOrder", MemoryKind.Rule, ["Approval", "Persetujuan"], "TO above 10M needs approval."));
        await memory.Service.AddShortAsync(new SaveMemoryRequest(MemoryType.Knowledge, null, "Caching", MemoryKind.Pattern, ["MasterData"], "Cache master data for 5 minutes."));

        Assert.Equal("github.com/org/eep", saved.ProjectKey);
        Assert.Contains(await memory.Service.GetIndexAsync(cloneB, "approval", 10), e => e.Id == saved.Id);
        Assert.DoesNotContain(await memory.Service.GetIndexAsync(other, "approval", 10), e => e.Id == saved.Id);
        Assert.Single(await memory.Service.GetIndexAsync(other, "master", 10));

        // After consolidation the long row keeps the key, so the other clone still sees it.
        Assert.Equal(2, await memory.Service.ConsolidateAsync());
        Assert.Contains(await memory.Service.GetIndexAsync(cloneB, "approval", 10), e => e.Id == saved.Id && e.Level == MemoryLevel.Long);
        var filtered = await memory.Service.QueryAsync(new MemoryQuery(null, null, cloneB, null));
        Assert.Equal(saved.Id, Assert.Single(filtered.Items).Id);
    }

    [Fact]
    public async Task Long_memory_keeps_the_git_origin_of_its_thread()
    {
        await using var harness = await OrchestrationHarness.CreateAsync();
        using var shortDb = new TempWorkspace();
        using var memory = await MemoryFixture.CreateAsync(harness, shortDb);
        await SetKeyAsync(harness, harness.ProjectId, "github.com/org/eep");
        var thread = new ThreadRun
        {
            ProjectId = harness.ProjectId, Prompt = "TO approval", Status = ThreadStatus.Completed,
            GitBranch = "feature/to-approval", GitBaseCommit = "0123456789abcdef0123456789abcdef01234567", GitAuthor = "Andi <andi@example.com>"
        };
        await using (var db = await harness.CreateDbAsync())
        {
            db.Threads.Add(thread);
            await db.SaveChangesAsync();
        }

        var saved = await memory.Service.AddShortAsync(new SaveMemoryRequest(MemoryType.Project, harness.ProjectId, "TransferOrder", MemoryKind.Rule, ["Approval"],
            "TO above 10M needs approval."), thread.Id);
        Assert.True(await memory.Service.PromoteAsync(saved.Id));

        var item = Assert.Single((await memory.Service.QueryAsync(new MemoryQuery(MemoryLevel.Long, null, harness.ProjectId, null))).Items);
        Assert.Equal(("Andi <andi@example.com>", "feature/to-approval", thread.GitBaseCommit), (item.GitAuthor, item.GitBranch, item.GitCommit));
    }

    [Fact]
    public async Task The_planner_skips_memory_whose_change_was_thrown_away_and_the_memory_menu_shows_the_git_state()
    {
        await using var harness = await OrchestrationHarness.CreateAsync();
        using var shortDb = new TempWorkspace();
        using var memory = await MemoryFixture.CreateAsync(harness, shortDb);
        await SetKeyAsync(harness, harness.ProjectId, "github.com/org/eep");
        var (discarded, committed) = (Guid.NewGuid(), Guid.NewGuid());
        await using (var shared = await harness.Service<LongMemoryStore>().CreateGitAsync())
        {
            shared.ThreadChanges.Add(new ThreadChange { Id = discarded, ProjectKey = "github.com/org/eep", State = GitChangeState.Discarded });
            shared.ThreadChanges.Add(new ThreadChange { Id = committed, ProjectKey = "github.com/org/eep", State = GitChangeState.Committed });
            await shared.SaveChangesAsync();
        }

        var gone = await memory.Service.AddShortAsync(new SaveMemoryRequest(MemoryType.Project, harness.ProjectId, "TransferOrder", MemoryKind.Rule,
            ["Approval"], "TO above 10M needs approval."), discarded);
        var kept = await memory.Service.AddShortAsync(new SaveMemoryRequest(MemoryType.Project, harness.ProjectId, "TransferOrder", MemoryKind.Rule,
            ["Approval", "Limit"], "TO above 20M needs two approvals."), committed);

        var index = await memory.Service.GetIndexAsync(harness.ProjectId, "approval", 10);
        Assert.DoesNotContain(index, e => e.Id == gone.Id);
        Assert.Contains(index, e => e.Id == kept.Id);

        var listed = (await memory.Service.QueryAsync(new MemoryQuery(null, null, harness.ProjectId, null))).Items;
        Assert.Equal(GitChangeState.Discarded, listed.Single(i => i.Id == gone.Id).GitState);
        Assert.Equal(GitChangeState.Committed, listed.Single(i => i.Id == kept.Id).GitState);
    }

    [Fact]
    public async Task Unreachable_long_memory_database_only_hides_long_memory()
    {
        await using var harness = await OrchestrationHarness.CreateAsync();
        using var shortDb = new TempWorkspace();
        using var memory = await MemoryFixture.CreateAsync(harness, shortDb);
        var saved = await memory.Service.AddShortAsync(new SaveMemoryRequest(MemoryType.Project, harness.ProjectId, "TransferOrder", MemoryKind.Rule, ["Approval"], "TO above 10M needs approval."));

        // Nothing listens on port 1: every connection is refused, as with a stopped PostgreSQL server.
        harness.Service<LongMemoryStore>().Configure(new MemoryStoreConnection(MemoryStoreProvider.PostgreSql, "127.0.0.1", 1, "aithread", "app", "pw"));

        Assert.Contains(await memory.Service.GetIndexAsync(harness.ProjectId, "approval", 10), e => e.Id == saved.Id);
        Assert.Equal(saved.Id, Assert.Single(await memory.Service.GetManyAsync([saved.Id, Guid.NewGuid()])).Id);

        // Consolidation waits for the database instead of failing record by record; the memory stays short.
        Assert.Equal(0, await memory.Service.ConsolidateAsync());
        Assert.Contains(await memory.Service.GetIndexAsync(harness.ProjectId, "approval", 10), e => e.Id == saved.Id && e.Level == MemoryLevel.Short);

        var stats = await memory.Service.GetStatsAsync();
        Assert.Equal((1, 0), (stats.ShortCount, stats.LongCount));
        Assert.EndsWith("tidak terhubung", stats.LongStore);
    }

    [Fact]
    public async Task Planner_index_and_read_memory_include_the_business_wiki_of_the_key()
    {
        await using var harness = await OrchestrationHarness.CreateAsync();
        using var shortDb = new TempWorkspace();
        using var memory = await MemoryFixture.CreateAsync(harness, shortDb);
        await SetKeyAsync(harness, harness.ProjectId, "github.com/org/eep");
        var other = await AddProjectAsync(harness, "Other", "github.com/org/other");
        var page = await harness.Service<IBusinessPageStore>().SaveAsync(new BusinessPageDraft(
            null, "github.com/org/eep", "EEP", "TO", "Transfer Order", null, "to, transfer order, approval",
            "# TO\n![Form TO](asset:3f2a1b9c-0000-4000-8000-000000000001)", true, null, BusinessEditKind.Ai, "dev", null, []), CancellationToken.None);

        var entry = Assert.Single(await memory.Service.GetIndexAsync(harness.ProjectId, "approval", 25));
        Assert.Equal((page.Id, MemoryLevel.Business, MemoryType.Business), (entry.Id, entry.Level, entry.Type));
        Assert.Empty(await memory.Service.GetIndexAsync(other, "approval", 25));

        var read = Assert.Single(await memory.Service.GetManyAsync([page.Id]));
        Assert.Equal("TO — Transfer Order", read.Label);
        Assert.Contains("[image: Form TO]", read.Description);
    }

    [Fact]
    public async Task Memory_written_before_keys_existed_is_shared_after_the_backfill()
    {
        await using var harness = await OrchestrationHarness.CreateAsync();
        using var shortDb = new TempWorkspace();
        using var memory = await MemoryFixture.CreateAsync(harness, shortDb);
        var cloneB = await AddProjectAsync(harness, "EEP (clone B)", "github.com/org/eep");
        await SetKeyAsync(harness, harness.ProjectId, "github.com/org/eep");

        var legacyId = Guid.NewGuid();
        await using (var db = await harness.CreateDbAsync())
        {
            db.LongMemories.Add(new LongMemory { Id = legacyId, ProjectId = harness.ProjectId, Type = MemoryType.Project, Label = "legacy", Description = "old row" });
            await db.SaveChangesAsync();
        }

        Assert.Contains(await memory.Service.GetIndexAsync(harness.ProjectId, "legacy", 10), e => e.Id == legacyId);
        Assert.DoesNotContain(await memory.Service.GetIndexAsync(cloneB, "legacy", 10), e => e.Id == legacyId);

        List<Project> projects;
        await using (var db = await harness.CreateDbAsync()) projects = await db.Projects.AsNoTracking().ToListAsync();
        Assert.Equal(1, await harness.Service<IProjectMemoryKeys>().BackfillAsync(projects, CancellationToken.None));

        Assert.Contains(await memory.Service.GetIndexAsync(cloneB, "legacy", 10), e => e.Id == legacyId);
    }

    [Fact]
    public async Task Machine_only_memory_follows_a_new_key_but_shared_memory_never_moves()
    {
        await using var harness = await OrchestrationHarness.CreateAsync();
        var projects = harness.Service<IProjectService>();

        await projects.RefreshKeysAsync();
        var localKey = (await projects.GetDetailAsync(harness.ProjectId))!.Project.Key;
        Assert.True(ProjectKeyResolver.IsLocal(localKey));
        var memoryId = await AddLongMemoryAsync(harness, localKey);

        var manual = await projects.SetKeyAsync(harness.ProjectId, "EEP");
        Assert.Equal(("eep", ProjectKeySource.Manual), (manual!.Key, manual.KeySource));
        Assert.Equal("eep", await LongMemoryKeyAsync(harness, memoryId));

        // The team commits an override file: the project follows it, but memory under the shared key "eep" stays,
        // since other developers' clones may still use that key.
        harness.Project.Write(".aithread/project.json", """{ "key": "eep-core" }""");
        await projects.RefreshKeysAsync();
        var detail = (await projects.GetDetailAsync(harness.ProjectId))!.Project;
        Assert.Equal(("eep-core", ProjectKeySource.File), (detail.Key, detail.KeySource));
        Assert.Equal("eep", await LongMemoryKeyAsync(harness, memoryId));
    }

    [Fact]
    public async Task Deleting_one_clone_hands_its_memory_to_the_other()
    {
        await using var harness = await OrchestrationHarness.CreateAsync();
        var projects = harness.Service<IProjectService>();
        await SetKeyAsync(harness, harness.ProjectId, "github.com/org/eep");
        var cloneB = await AddProjectAsync(harness, "EEP (clone B)", "github.com/org/eep");
        var memoryId = Guid.NewGuid();
        await using (var db = await harness.CreateDbAsync())
        {
            db.LongMemories.Add(new LongMemory { Id = memoryId, ProjectId = harness.ProjectId, ProjectKey = "github.com/org/eep", Type = MemoryType.Project, Label = "l", Description = "d" });
            await db.SaveChangesAsync();
        }

        Assert.True(await projects.DeleteAsync(harness.ProjectId));

        await using (var db = await harness.CreateDbAsync())
        {
            var row = await db.LongMemories.AsNoTracking().SingleAsync(m => m.Id == memoryId);
            Assert.Equal(cloneB, row.ProjectId);
        }
    }

    [Fact]
    public async Task Shared_store_keeps_project_memory_unless_asked()
    {
        using var workspace = new TempWorkspace();
        var store = new LongMemoryStore(new UnusedMainDatabase());
        store.Configure(new MemoryStoreConnection(MemoryStoreProvider.Sqlite, Database: Path.Combine(workspace.Root, "shared.db"), Options: "Pooling=False"));
        var keys = new ProjectMemoryKeys(new NoShortMemory(), store, NullLogger<ProjectMemoryKeys>.Instance);
        var projectId = Guid.NewGuid();
        await using (var db = await store.CreateAsync())
        {
            db.LongMemories.Add(new LongMemory { Id = Guid.NewGuid(), ProjectKey = "github.com/org/eep", Type = MemoryType.Project, Label = "shared", Description = "d" });
            db.LongMemories.Add(new LongMemory { Id = Guid.NewGuid(), ProjectKey = "local:abc", Type = MemoryType.Project, Label = "mine", Description = "d" });
            await db.SaveChangesAsync();
        }

        Assert.Equal(0, await keys.DeleteAsync(projectId, "github.com/org/eep", includeShared: false, CancellationToken.None));
        Assert.Equal(1, await keys.DeleteAsync(projectId, "local:abc", includeShared: false, CancellationToken.None));
        Assert.Equal(1, await keys.DeleteAsync(projectId, "github.com/org/eep", includeShared: true, CancellationToken.None));
    }

    private static async Task<Guid> AddProjectAsync(OrchestrationHarness harness, string name, string key)
    {
        await using var db = await harness.CreateDbAsync();
        var project = new Project { Name = name, RootPath = Path.Combine(harness.Project.Root, "..", Guid.NewGuid().ToString("N")), ProjectKey = key, ProjectKeySource = ProjectKeySource.GitRemote };
        db.Projects.Add(project);
        await db.SaveChangesAsync();
        return project.Id;
    }

    private static async Task SetKeyAsync(OrchestrationHarness harness, Guid projectId, string key)
    {
        await using var db = await harness.CreateDbAsync();
        await db.Projects.Where(p => p.Id == projectId).ExecuteUpdateAsync(s => s.SetProperty(p => p.ProjectKey, key));
    }

    private static async Task<Guid> AddLongMemoryAsync(OrchestrationHarness harness, string key)
    {
        await using var db = await harness.CreateDbAsync();
        var row = new LongMemory { Id = Guid.NewGuid(), ProjectId = harness.ProjectId, ProjectKey = key, Type = MemoryType.Project, Label = "l", Description = "d" };
        db.LongMemories.Add(row);
        await db.SaveChangesAsync();
        return row.Id;
    }

    private static async Task<string?> LongMemoryKeyAsync(OrchestrationHarness harness, Guid id)
    {
        await using var db = await harness.CreateDbAsync();
        return await db.LongMemories.AsNoTracking().Where(m => m.Id == id).Select(m => m.ProjectKey).SingleAsync();
    }

    // The real MemoryService over the harness's main database, with short memory in a SQLite file.
    private sealed class MemoryFixture : IDisposable
    {
        private readonly RedisConnectionProvider _redis;
        private readonly SwitchableShortMemoryStore _short;

        private MemoryFixture(RedisConnectionProvider redis, SwitchableShortMemoryStore shortStore, MemoryService service)
        {
            _redis = redis;
            _short = shortStore;
            Service = service;
        }

        public MemoryService Service { get; }

        public static Task<MemoryFixture> CreateAsync(OrchestrationHarness harness, TempWorkspace shortDb)
        {
            var redis = new RedisConnectionProvider("127.0.0.1:1", NullLogger<RedisConnectionProvider>.Instance);
            var shortStore = new SwitchableShortMemoryStore(redis, "127.0.0.1:1", NullLoggerFactory.Instance);
            shortStore.Configure(new MemoryStoreConnection(MemoryStoreProvider.Sqlite, Database: Path.Combine(shortDb.Root, "short.db"), Options: "Pooling=False"));

            var service = new MemoryService(
                harness.Service<IDbContextFactory<AIThreadDbContext>>(),
                shortStore,
                harness.Service<LongMemoryStore>(),
                harness.Service<BusinessMemoryStore>(),
                harness.Service<IModuleRegistry>(),
                harness.Service<IGitChangeTracker>(),
                harness.Service<IEventBus>(),
                harness.Service<IActivityLogService>(),
                Options.Create(new AIThreadOptions { ShortMemoryHours = 0 }),
                NullLogger<MemoryService>.Instance);
            return Task.FromResult(new MemoryFixture(redis, shortStore, service));
        }

        public void Dispose()
        {
            _short.Dispose();
            _redis.Dispose();
        }
    }

    private sealed class NoShortMemory : IShortMemoryStore
    {
        public bool IsConnected => false;
        public Task SaveAsync(ShortMemoryRecord record) => Task.CompletedTask;
        public Task<ShortMemoryRecord?> GetAsync(Guid id) => Task.FromResult<ShortMemoryRecord?>(null);
        public Task<IReadOnlyList<ShortMemoryRecord>> GetAllAsync() => Task.FromResult<IReadOnlyList<ShortMemoryRecord>>([]);
        public Task<IReadOnlyList<ShortMemoryRecord>> GetCreatedBeforeAsync(DateTime cutoffUtc) => Task.FromResult<IReadOnlyList<ShortMemoryRecord>>([]);
        public Task<bool> DeleteAsync(Guid id) => Task.FromResult(false);
    }

    private sealed class UnusedMainDatabase : IDbContextFactory<AIThreadDbContext>
    {
        public AIThreadDbContext CreateDbContext() => throw new InvalidOperationException("The main database must not be used.");
    }
}

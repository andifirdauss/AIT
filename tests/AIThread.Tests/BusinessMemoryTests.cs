using System.Text.Json.Nodes;
using AIThread.Common.Entities;
using AIThread.Common.Enums;
using AIThread.Data.Context;
using AIThread.Data.Memory;
using AIThread.Service.Business;
using AIThread.Service.Interfaces;
using AIThread.Service.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Tests;

public class BusinessMemoryTests
{
    private const string Key = "github.com/org/eep";

    // PNG signature plus a few bytes: enough for the type check.
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, 0x49, 0x48, 0x44, 0x52];

    [Fact]
    public void Rendering_drops_raw_html_and_unsafe_links()
    {
        var html = BusinessMarkdown.ToHtml("""
            <script>alert(1)</script>
            [klik](javascript:alert(1)) [web](https://example.com) <javascript:alert(2)>

            ![luar](https://tracker.example/x.png) ![form](asset:3f2a1b9c-0000-4000-8000-000000000001)

            | Status | Arti |
            |---|---|
            | Draft | Belum diajukan |
            """);

        Assert.DoesNotContain("<script", html);
        Assert.DoesNotContain("javascript:", html);
        Assert.Contains("href=\"https://example.com\"", html);
        Assert.DoesNotContain("tracker.example", html);
        Assert.Contains("src=\"/api/business/assets/3f2a1b9c-0000-4000-8000-000000000001\"", html);
        Assert.Contains("<table", html);
    }

    [Fact]
    public void Planner_reads_images_as_captions()
    {
        var text = BusinessMarkdown.ForPlanner("Form:\n![Form TO](asset:3f2a1b9c-0000-4000-8000-000000000001)");
        Assert.Equal("Form:\n[image: Form TO]", text);
    }

    [Theory]
    [InlineData("TO", "Transfer Order", "to-transfer-order")]
    [InlineData("SO/Return", "Retur Penjualan (Draft)", "so-return-retur-penjualan-draft")]
    [InlineData("??", "!!", "page")]
    public void Slugs_are_url_safe(string module, string title, string expected) =>
        Assert.Equal(expected, BusinessMarkdown.Slug(module, title));

    [Fact]
    public void Only_real_images_are_accepted()
    {
        Assert.Equal("image/png", BusinessImages.Validate(Png));
        Assert.Throws<AppValidationException>(() => BusinessImages.Validate("<svg onload=alert(1)>"u8.ToArray()));
        Assert.Throws<AppValidationException>(() => BusinessImages.Validate(new byte[BusinessImages.MaxBytes + 1]));
    }

    [Fact]
    public async Task Saving_over_a_newer_version_is_refused_and_every_save_is_a_revision()
    {
        await using var harness = await OrchestrationHarness.CreateAsync();
        var pages = harness.Service<IBusinessPageStore>();

        var page = await pages.SaveAsync(Draft(null, "v1", null), CancellationToken.None);
        var second = await pages.SaveAsync(Draft(page.Id, "v2", expectedVersion: 1), CancellationToken.None);
        Assert.Equal(2, second.Version);

        // Someone else saved version 2 meanwhile; this editor still has version 1.
        await Assert.ThrowsAsync<BusinessPageConflictException>(() => pages.SaveAsync(Draft(page.Id, "stale", expectedVersion: 1), CancellationToken.None));

        await using var db = await harness.CreateDbAsync();
        Assert.Equal(2, await db.BusinessPageRevisions.CountAsync(r => r.PageId == page.Id));
        Assert.Equal("v2", (await db.BusinessPages.SingleAsync()).Content);
    }

    [Fact]
    public async Task Same_image_is_stored_once_per_project()
    {
        await using var harness = await OrchestrationHarness.CreateAsync();
        var pages = harness.Service<IBusinessPageStore>();

        var first = await pages.AddAssetAsync(Key, null, Png, "a", null, "dev", CancellationToken.None);
        var again = await pages.AddAssetAsync(Key, null, Png, "b", null, "dev", CancellationToken.None);
        var otherProject = await pages.AddAssetAsync("github.com/org/other", null, Png, "c", null, "dev", CancellationToken.None);

        Assert.Equal(first.Id, again.Id);
        Assert.NotEqual(first.Id, otherProject.Id);
    }

    [Fact]
    public async Task Finished_threads_become_a_business_page_with_their_screenshot()
    {
        await using var harness = await OrchestrationHarness.CreateAsync();
        await harness.AddNodeAsync(NodeRole.Planner, "planner-model");
        await SetProjectKeyAsync(harness);
        var threadId = await AddFinishedThreadAsync(harness, "Tambah approval TO di atas 10 juta", withImage: true);

        harness.Router.On(ScriptedAgent.BusinessRouter, ScriptedRouter.Text(
            """{"pages":[{"id":null,"module":"TO","title":"Transfer Order","threads":["T1"]}]}"""));
        harness.Router.On(ScriptedAgent.BusinessWriter, ScriptedRouter.Text(JsonSerializerText(new JsonObject
        {
            ["content"] = "# TO — Transfer Order\n\n## Aturan bisnis\n\n1. **BR-TO-01** — TO di atas 10 juta perlu approval.\n\n## Tampilan\n\n![Form TO](asset:img1)\n\n![hilang](asset:img7)",
            ["summary"] = "Pemindahan stok antar gudang.",
            ["labels"] = "to, transfer order, approval",
            ["changeNote"] = "Tambah aturan approval"
        })));

        var result = await harness.Service<IBusinessDigestRunner>().RunProjectAsync(harness.ProjectId, CancellationToken.None);

        Assert.Equal((1, 1, 0, 1), (result.Threads, result.PagesCreated, result.PagesUpdated, result.Images));
        var writerCall = Assert.Single(harness.Router.CallsTo(ScriptedAgent.BusinessWriter));
        Assert.Contains("image_url", writerCall.Messages.ToJsonString());

        await using var db = await harness.CreateDbAsync();
        var page = await db.BusinessPages.SingleAsync();
        var asset = await db.BusinessAssets.SingleAsync();
        Assert.Equal((Key, "TO", "to-transfer-order", BusinessEditKind.Ai), (page.ProjectKey, page.Module, page.Slug, page.LastEditKind));
        Assert.Contains($"asset:{asset.Id}", page.Content);
        Assert.DoesNotContain("asset:img", page.Content);
        Assert.Equal(page.Id, asset.PageId);
        Assert.Equal(threadId.ToString(), (await db.BusinessPageRevisions.SingleAsync()).SourceThreadIds);
        Assert.NotNull((await db.Threads.SingleAsync(t => t.Id == threadId)).BusinessDigestedAt);
        Assert.Empty(await db.ThreadAttachments.ToListAsync());

        var again = await harness.Service<IBusinessDigestRunner>().RunProjectAsync(harness.ProjectId, CancellationToken.None);
        Assert.Equal(0, again.Threads);
    }

    [Fact]
    public async Task Hand_curated_pages_are_left_alone_and_the_thread_is_still_done()
    {
        await using var harness = await OrchestrationHarness.CreateAsync();
        await harness.AddNodeAsync(NodeRole.Planner, "planner-model");
        await SetProjectKeyAsync(harness);
        var locked = await harness.Service<IBusinessPageStore>().SaveAsync(Draft(null, "curated", null) with { AutoUpdate = false }, CancellationToken.None);
        var threadId = await AddFinishedThreadAsync(harness, "Ubah label tombol TO", withImage: false);

        harness.Router.On(ScriptedAgent.BusinessRouter, ScriptedRouter.Text(
            $$"""{"pages":[{"id":"{{locked.Id}}","module":"TO","title":"Transfer Order","threads":["T1"]}]}"""));

        var result = await harness.Service<IBusinessDigestRunner>().RunProjectAsync(harness.ProjectId, CancellationToken.None);

        Assert.Equal((1, 0, 0), (result.Threads, result.PagesCreated, result.PagesUpdated));
        Assert.Empty(harness.Router.CallsTo(ScriptedAgent.BusinessWriter));
        await using var db = await harness.CreateDbAsync();
        Assert.Equal("curated", (await db.BusinessPages.SingleAsync()).Content);
        Assert.NotNull((await db.Threads.SingleAsync(t => t.Id == threadId)).BusinessDigestedAt);
    }

    [Fact]
    public async Task A_page_the_writer_cannot_produce_keeps_its_threads_for_the_next_run()
    {
        await using var harness = await OrchestrationHarness.CreateAsync();
        await harness.AddNodeAsync(NodeRole.Planner, "planner-model");
        await SetProjectKeyAsync(harness);
        var threadId = await AddFinishedThreadAsync(harness, "Tambah status TO", withImage: false);

        harness.Router.On(ScriptedAgent.BusinessRouter, ScriptedRouter.Text(
            """{"pages":[{"id":null,"module":"TO","title":"Transfer Order","threads":["T1"]}]}"""));
        harness.Router.On(ScriptedAgent.BusinessWriter, ScriptedRouter.Text("sorry, no JSON"));

        var result = await harness.Service<IBusinessDigestRunner>().RunProjectAsync(harness.ProjectId, CancellationToken.None);

        Assert.Equal(0, result.Threads);
        Assert.Single(result.Warnings);
        await using var db = await harness.CreateDbAsync();
        Assert.Null((await db.Threads.SingleAsync(t => t.Id == threadId)).BusinessDigestedAt);
        Assert.Empty(await db.BusinessPages.ToListAsync());
    }

    [Fact]
    public async Task Planner_index_lists_the_projects_business_pages()
    {
        await using var harness = await OrchestrationHarness.CreateAsync();
        await SetProjectKeyAsync(harness);
        var page = await harness.Service<IBusinessPageStore>().SaveAsync(
            Draft(null, "# TO\n![Form TO](asset:3f2a1b9c-0000-4000-8000-000000000001)", null), CancellationToken.None);
        var business = harness.Service<IBusinessMemoryService>();

        var pages = await business.GetPagesAsync(harness.ProjectId);
        var read = await business.GetForPlannerAsync([page.Id]);

        Assert.Equal(page.Id, Assert.Single(pages).Id);
        Assert.Contains("[image: Form TO]", Assert.Single(read).Content);
    }

    [Fact]
    public async Task External_memory_table_from_an_older_version_gets_the_new_columns()
    {
        using var workspace = new TempWorkspace();
        var file = Path.Combine(workspace.Root, "memory.db");
        await using (var connection = new SqliteConnection($"Data Source={file};Pooling=False"))
        {
            await connection.OpenAsync();
            var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE "LongMemories" (
                    "Id" TEXT NOT NULL CONSTRAINT "PK_LongMemories" PRIMARY KEY,
                    "ProjectId" TEXT NULL, "Type" TEXT NOT NULL, "Label" TEXT NOT NULL, "Description" TEXT NOT NULL,
                    "SourceThreadId" TEXT NULL, "PromotedAt" TEXT NOT NULL, "HitCount" INTEGER NOT NULL,
                    "LastAccessedAt" TEXT NULL, "CreatedAt" TEXT NOT NULL, "UpdatedAt" TEXT NULL);
                INSERT INTO "LongMemories" VALUES ('0b5c6a1e-0000-4000-8000-000000000001', NULL, 'Knowledge', 'old', 'kept',
                    NULL, '2026-01-01 00:00:00', 0, NULL, '2026-01-01 00:00:00', NULL);
                """;
            await command.ExecuteNonQueryAsync();
        }

        var store = new LongMemoryStore(new UnusedMainDatabase());
        store.Configure(new MemoryStoreConnection(MemoryStoreProvider.Sqlite, Database: file, Options: "Pooling=False"));
        await using (var db = await store.CreateAsync())
        {
            db.LongMemories.Add(new LongMemory { Id = Guid.NewGuid(), ProjectKey = Key, ProjectName = "EEP", Type = MemoryType.Project, Label = "new", Description = "d" });
            await db.SaveChangesAsync();
        }

        await using (var db = await store.CreateAsync())
        {
            var rows = await db.LongMemories.AsNoTracking().OrderBy(m => m.Label).ToListAsync();
            Assert.Equal(["new", "old"], rows.Select(r => r.Label));
            Assert.Equal(Key, rows[0].ProjectKey);
            Assert.Null(rows[1].ProjectKey);
        }
    }

    private static BusinessPageDraft Draft(Guid? id, string content, int? expectedVersion) => new(
        id, Key, "EEP", "TO", "Transfer Order", null, "to, transfer order", content, true, expectedVersion,
        BusinessEditKind.Manual, "tester", null, []);

    private static async Task SetProjectKeyAsync(OrchestrationHarness harness)
    {
        await using var db = await harness.CreateDbAsync();
        await db.Projects.Where(p => p.Id == harness.ProjectId).ExecuteUpdateAsync(s => s
            .SetProperty(p => p.ProjectKey, Key)
            .SetProperty(p => p.ProjectKeySource, ProjectKeySource.GitRemote));
    }

    private static async Task<Guid> AddFinishedThreadAsync(OrchestrationHarness harness, string prompt, bool withImage)
    {
        await using var db = await harness.CreateDbAsync();
        var thread = new ThreadRun
        {
            ProjectId = harness.ProjectId,
            Source = ThreadSource.ClaudeCode,
            Prompt = prompt,
            Status = ThreadStatus.Completed,
            PlanSummary = prompt,
            FinalResponse = "Selesai.",
            CompletedAt = DateTime.UtcNow
        };
        db.Threads.Add(thread);
        if (withImage) db.ThreadAttachments.Add(new ThreadAttachment { ThreadRunId = thread.Id, MediaType = "image/png", Data = Png, Sha256 = BusinessImages.Sha256(Png) });
        await db.SaveChangesAsync();
        return thread.Id;
    }

    private static string JsonSerializerText(JsonObject json) => json.ToJsonString();

    private sealed class UnusedMainDatabase : IDbContextFactory<AIThreadDbContext>
    {
        public AIThreadDbContext CreateDbContext() => throw new InvalidOperationException("The main database must not be used.");
    }
}

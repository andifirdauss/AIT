using System.Text.Json.Nodes;
using AIThread.Common.DTOs;
using AIThread.Common.Entities;
using AIThread.Common.Enums;
using AIThread.Common.Memory;
using AIThread.Service.Interfaces;
using AIThread.Service.Memory;
using AIThread.Service.Orchestration;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Tests;

public class ChangeLogTests
{
    [Fact]
    public void Entry_is_rendered_with_the_changed_files_and_the_models_notes()
    {
        var entry = ChangeLog.ParseEntry(JsonNode.Parse("""
            {"title": "Tambah kolom Deskripsi", "feature": "User bisa mengisi deskripsi.",
             "files": [{"path": "./Views\\User\\Index.cshtml", "change": "input `#txtDescription`"}, {"path": "src/Unrelated.cs", "change": "x"}],
             "validation": ["- `Description` maks 500 karakter"], "flow": ["1. `user.js` kirim `description`", "2. `UserService.Save` validasi"],
             "notes": []}
            """)!.AsObject())!;
        var changed = new List<ChangeLogFile>
        {
            new("Views/User/Index.cshtml", ChangeLog.Modified, null),
            new("Migrations/AddDescription.cs", ChangeLog.Created, null)
        };

        var text = ChangeLog.Render(entry with { Files = ChangeLog.MergeFiles(changed, entry.Files) });

        Assert.Equal("""
            ### Tambah kolom Deskripsi
            **Fitur:** User bisa mengisi deskripsi.

            **File berubah:**
            - `Views/User/Index.cshtml` (ubah) — input `#txtDescription`
            - `Migrations/AddDescription.cs` (baru)

            **Validasi:**
            - `Description` maks 500 karakter

            **Flow:**
            1. `user.js` kirim `description`
            2. `UserService.Save` validasi
            """.ReplaceLineEndings("\n"), text);
    }

    [Fact]
    public void Entry_without_a_title_is_dropped()
    {
        Assert.Null(ChangeLog.ParseEntry(JsonNode.Parse("""{"title": " ", "files": []}""")!.AsObject()));
    }

    [Fact]
    public async Task Change_entries_and_older_facts_are_folded_into_the_module_summary()
    {
        await using var harness = await OrchestrationHarness.CreateAsync();
        await SetKeyAsync(harness, "k");
        await harness.AddNodeAsync(NodeRole.ToolsNode, "tools-model");
        using var backup = new TempWorkspace();
        MemoryRelabeler.BackupDirectory = backup.Root;
        var t0 = DateTime.UtcNow.AddHours(-2);
        await AddAsync(harness, "User", MemoryKind.Change, "### Tambah deskripsi", t0);
        await AddAsync(harness, "Order", MemoryKind.Change, "### Order lain", t0, extraModules: "User");
        var fact = await AddAsync(harness, "User", MemoryKind.Rule, "Aturan: `Email` wajib unik.", t0.AddMinutes(-5));
        var writer = harness.Service<IModuleSummaryWriter>();
        harness.Router.On(ScriptedAgent.ModuleSummary, call =>
        {
            Assert.Contains("# Module\nUser\n", call.LastUser);
            Assert.Contains("# Current state\n(none)", call.LastUser);
            Assert.Contains("### Tambah deskripsi", call.LastUser);
            Assert.Contains("### Order lain", call.LastUser);
            Assert.Contains("- [Rule] Aturan: `Email` wajib unik.", call.LastUser);
            return ScriptedRouter.Text("""
                {"features": ["Kelola user"], "files": [{"path": "Services/UserService.cs", "role": "validasi & simpan"}],
                 "validation": ["`Email` wajib unik", "`Description` maks 500"], "flow": [], "notes": [], "keywords": ["user", "pengguna"]}
                """);
        });
        harness.Router.On(ScriptedAgent.ModuleSummary, ScriptedRouter.Text("""{"features": ["Kelola order"], "keywords": ["order"]}"""));

        Assert.Equal(2, await writer.RunAsync(default));

        var summary = await SummaryAsync(harness, "User");
        Assert.Equal(t0, summary.PromotedAt, TimeSpan.FromMilliseconds(1));
        Assert.Equal("""
            ### User — kondisi saat ini

            **Fitur:**
            - Kelola user

            **File utama:**
            - `Services/UserService.cs` — validasi & simpan

            **Validasi:**
            - `Email` wajib unik
            - `Description` maks 500
            """.ReplaceLineEndings("\n"), summary.Description);
        Assert.Equal("User · Summary | Pengguna", summary.Label);
        Assert.NotNull(await SummaryAsync(harness, "Order"));
        Assert.Null(await FindAsync(harness, fact));
        Assert.Single(Directory.GetFiles(backup.Root, $"{ModuleSummaryWriter.BackupPrefix}*.json"));

        // Nothing new: no model call. A newer entry is folded on top of the current state.
        Assert.Equal(0, await writer.RunAsync(default));
        await AddAsync(harness, "User", MemoryKind.Change, "### Hapus deskripsi", DateTime.UtcNow);
        harness.Router.On(ScriptedAgent.ModuleSummary, call =>
        {
            Assert.Contains("# Current state\n### User — kondisi saat ini", call.LastUser);
            Assert.Contains("### Hapus deskripsi", call.LastUser);
            Assert.DoesNotContain("### Tambah deskripsi", call.LastUser);
            return ScriptedRouter.Text("""{"features": ["Kelola user tanpa deskripsi"]}""");
        });

        Assert.Equal(1, await writer.RunAsync(default));
        Assert.Equal(3, harness.Router.CallsTo(ScriptedAgent.ModuleSummary).Count);
        Assert.Contains("Kelola user tanpa deskripsi", (await SummaryAsync(harness, "User")).Description);
    }

    [Fact]
    public async Task Planner_brief_writes_out_the_module_memory()
    {
        await using var harness = await ThreadOrchestratorTests.CreateAsync();
        var thread = Guid.NewGuid();
        harness.Memory.ModuleMemory.Add(new MemoryItemDto(Guid.NewGuid(), MemoryLevel.Short, MemoryType.Project, harness.ProjectId, null, "User · Change",
            "### Tambah deskripsi\n**Fitur:** User bisa mengisi deskripsi.", thread, new DateTime(2026, 10, 25, 0, 0, 0, DateTimeKind.Utc), null, 0,
            Module: "User", Kind: MemoryKind.Change, Labeled: true));
        harness.Router.On(ScriptedAgent.Planner, harness.Router.SubmitPlan(new ThreadPlan { Summary = "Jawab.", Answer = "Sudah ada." }));

        await harness.RunAsync("Apa saja yang berubah di user?");

        var brief = harness.Router.CallsTo(ScriptedAgent.Planner)[0].LastUser;
        Assert.Contains($"# Recent changes in this project (newest first)\n## User — change 2026-10-25 (thread {ThreadAnchor.ShortId(thread)})\n### Tambah deskripsi\n", brief);
        Assert.True(brief.IndexOf("# Recent changes", StringComparison.Ordinal) < brief.IndexOf("# Memory index", StringComparison.Ordinal));
    }

    private static async Task SetKeyAsync(OrchestrationHarness harness, string key)
    {
        await using var db = await harness.CreateDbAsync();
        await db.Projects.Where(p => p.Id == harness.ProjectId).ExecuteUpdateAsync(s => s.SetProperty(p => p.ProjectKey, key));
    }

    private static async Task<Guid> AddAsync(OrchestrationHarness harness, string module, MemoryKind kind, string description, DateTime promotedAt,
        string? extraModules = null)
    {
        await using var db = await harness.CreateDbAsync();
        var row = new LongMemory
        {
            Id = Guid.NewGuid(), ProjectId = harness.ProjectId, ProjectKey = "k", ProjectName = "demo", Type = MemoryType.Project, Description = description,
            Module = module, ExtraModules = extraModules, Kind = kind, Keywords = "Kw", LabelVersion = MemoryLabels.CurrentVersion,
            Label = MemoryLabels.Compose(MemoryLabels.Display(null, module, [], kind), ["Kw"]), CreatedAt = promotedAt, PromotedAt = promotedAt
        };
        db.LongMemories.Add(row);
        await db.SaveChangesAsync();
        return row.Id;
    }

    private static async Task<LongMemory> SummaryAsync(OrchestrationHarness harness, string module)
    {
        await using var db = await harness.CreateDbAsync();
        return await db.LongMemories.AsNoTracking().SingleAsync(m => m.Kind == MemoryKind.Summary && m.Module == module);
    }

    private static async Task<LongMemory?> FindAsync(OrchestrationHarness harness, Guid id)
    {
        await using var db = await harness.CreateDbAsync();
        return await db.LongMemories.AsNoTracking().SingleOrDefaultAsync(m => m.Id == id);
    }
}

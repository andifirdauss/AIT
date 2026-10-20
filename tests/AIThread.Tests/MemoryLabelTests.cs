using AIThread.Common.DTOs;
using AIThread.Common.Entities;
using AIThread.Common.Enums;
using AIThread.Common.Memory;
using AIThread.Common.Options;
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
using Microsoft.Extensions.Options;

namespace AIThread.Tests;

// The structured memory label: normalization, the code map rules, the two search layers, the module list with its
// merges, and the relabeling of memory written by older versions.
public class MemoryLabelTests
{
    [Theory]
    [InlineData("user", "User")]
    [InlineData("USER", "User")]
    [InlineData("users table", "UsersTable")]
    [InlineData("api client", "APIClient")]
    [InlineData("user dto", "UserDTO")]
    [InlineData("ui", "UI")]
    [InlineData("UserDto", "UserDto")]
    [InlineData("UserBL", "UserBL")]
    [InlineData("userStore", "UserStore")]
    [InlineData("transfer-order", "TransferOrder")]
    [InlineData("TO", "TO")]
    [InlineData("unit price", "UnitPrice")]
    public void Pascal_keeps_acronyms_in_capitals_and_identifiers_as_written(string input, string expected) =>
        Assert.Equal(expected, MemoryLabels.Pascal(input));

    [Theory]
    [InlineData("MKPP", "MKPP")]
    [InlineData("KlaimDF", "KlaimDF")]
    [InlineData("Klaim DF", "KlaimDF")]
    [InlineData("klaimdf", "Klaimdf")]
    [InlineData("MKPP approval", "MKPPApproval")]
    [InlineData("transfer order", "TransferOrder")]
    [InlineData("user dto", "UserDTO")]
    public void Module_names_keep_their_abbreviations_in_capitals(string input, string expected) =>
        Assert.Equal(expected, MemoryLabels.Module(input));

    [Fact]
    public void Extra_modules_drop_the_main_module_and_duplicates_and_show_in_the_label()
    {
        var extra = MemoryLabels.ExtraModules(["invoice", "Users", "Invoices", "payment", "tax", "shipping"], "User");

        Assert.Equal(["Invoice", "Payment", "Tax"], extra);
        Assert.Equal("Order+Invoice · Logic · Flow", MemoryLabels.Display(null, "Order", [MemoryLayer.Logic], MemoryKind.Flow, ["Invoice"]));
        Assert.True(MemoryLabels.HasModule("Order", "Invoice,Payment", "payment"));
        Assert.False(MemoryLabels.HasModule("Order", "Invoice", "Pay"));
    }

    [Fact]
    public void Ranking_narrowed_by_module_finds_memory_of_that_module_as_an_extra_one()
    {
        var shared = Entry("Order", MemoryKind.Flow, ["Posting"]) with { ExtraModules = ["Invoice"] };
        var invoiceOnly = Entry("Invoice", MemoryKind.Rule, ["Number"]);
        var orderOnly = Entry("Order", MemoryKind.Rule, ["Status"]);

        var ranked = MemoryRanker.RankLabeled([shared, invoiceOnly, orderOnly], ["Invoice"], null, null, [], 10);

        Assert.Equal(new[] { invoiceOnly.Id, shared.Id }.Order(), ranked.Select(e => e.Id).Order());
    }

    [Fact]
    public void Keywords_are_normalized_deduplicated_cut_at_sixteen_and_never_repeat_the_label()
    {
        var raw = new[] { "description", "Description", "deskripsi", "User", "Logic", "rule" }
            .Concat(Enumerable.Range(1, 30).Select(i => $"word{i}"));

        var keywords = MemoryLabels.Keywords(raw, "User");

        Assert.Equal(MemoryLabels.MaxKeywords, keywords.Count);
        Assert.Equal(["Description", "Deskripsi", "Word1"], keywords.Take(3));
        Assert.DoesNotContain("User", keywords);
        Assert.DoesNotContain("Logic", keywords);
        Assert.DoesNotContain("Rule", keywords);
    }

    [Fact]
    public void Configured_acronyms_are_written_in_capitals()
    {
        MemoryLabels.AddAcronyms(["sku"]);
        Assert.Equal("ProductSKU", MemoryLabels.Pascal("product sku"));
    }

    [Theory]
    [InlineData("validation", MemoryKind.Rule)]
    [InlineData("Pitfall", MemoryKind.Pitfall)]
    [InlineData("bug", MemoryKind.Pitfall)]
    [InlineData("design", MemoryKind.Architecture)]
    public void Kinds_accept_their_names_and_common_synonyms(string text, MemoryKind expected)
    {
        Assert.True(MemoryLabels.TryParseKind(text, out var kind));
        Assert.Equal(expected, kind);
    }

    [Fact]
    public void Knowledge_memory_only_takes_kinds_that_are_not_project_specific()
    {
        Assert.True(MemoryLabels.KindAllowedFor(MemoryType.Knowledge, MemoryKind.Pattern));
        Assert.False(MemoryLabels.KindAllowedFor(MemoryType.Knowledge, MemoryKind.Rule));
        Assert.True(MemoryLabels.KindAllowedFor(MemoryType.Project, MemoryKind.Rule));
    }

    [Fact]
    public void Tokens_split_identifiers_at_their_humps_and_drop_common_words()
    {
        var tokens = MemoryLabels.Tokens("tolong tambah validasi di UserProfileService untuk UI");

        Assert.Contains("userprofileservice", tokens);
        Assert.Contains("profile", tokens);
        Assert.Contains("validasi", tokens);
        Assert.Contains("ui", tokens);
        Assert.DoesNotContain("tolong", tokens);
        Assert.DoesNotContain("untuk", tokens);
    }

    [Theory]
    [InlineData("User", "Users", true)]
    [InlineData("Category", "categories", true)]
    [InlineData("transfer_order", "TransferOrder", true)]
    [InlineData("User", "UserRole", false)]
    [InlineData("Kas", "Ka", false)]
    public void Same_name_ignores_case_separators_and_english_plurals(string a, string b, bool same) =>
        Assert.Equal(same, MemoryLabels.SameName(a, b));

    [Theory]
    [InlineData("src/Web/Controllers/UserController.cs", MemoryLayer.Entry)]
    [InlineData("src/Bl/UserBL.cs", MemoryLayer.Logic)]
    [InlineData("src/Data/UserRepository.cs", MemoryLayer.Data)]
    [InlineData("src/Web/Views/User/Index.cshtml", MemoryLayer.UI)]
    [InlineData("client/src/user.service.ts", MemoryLayer.Logic)]
    [InlineData("client/src/pages/UserPage.tsx", MemoryLayer.UI)]
    [InlineData("tests/UserServiceTests.cs", MemoryLayer.Test)]
    [InlineData("src/Web/Program.cs", MemoryLayer.Infra)]
    [InlineData("src/Data/Migrations/20260101_Init.cs", MemoryLayer.Data)]
    [InlineData("db/create_users.sql", MemoryLayer.Data)]
    public void Layer_rules_follow_the_role_of_the_file_not_its_side(string path, MemoryLayer expected) =>
        Assert.Equal(expected, CodeMapHeuristics.Layer(path));

    [Theory]
    [InlineData("src/Web/Controllers/UserController.cs", "User")]
    [InlineData("src/Bl/UserBL.cs", "User")]
    [InlineData("src/Services/IUserService.cs", "User")]
    [InlineData("src/Web/Views/TransferOrder/Index.cshtml", "TransferOrder")]
    [InlineData("client/src/user-profile.component.ts", "UserProfile")]
    [InlineData("src/Web/Program.cs", "Program")]
    [InlineData("src/Web/Views/Shared/_Layout.cshtml", null)]
    public void Module_stem_strips_the_role_words_and_falls_back_to_the_feature_folder(string path, string? expected) =>
        Assert.Equal(expected, CodeMapHeuristics.ModuleStem(path));

    [Fact]
    public void Areas_appear_only_in_a_repository_with_several_apps()
    {
        var solution = new ProjectContext(Guid.NewGuid(), "AIT", "C:/ait", ["dotnet"],
            ["AIThread.slnx", "src/AIThread.UI/AIThread.UI.csproj", "src/AIThread.API/AIThread.API.csproj"], "k");
        Assert.Empty(CodeMapHeuristics.AreaFolders(solution));

        var monorepo = new ProjectContext(Guid.NewGuid(), "Shop", "C:/shop", ["node", "dotnet"],
            ["apps/web/package.json", "apps/api/Shop.Api.csproj"], "k");
        var areas = CodeMapHeuristics.AreaFolders(monorepo);
        Assert.Equal("Web", CodeMapHeuristics.AreaOf("apps/web/src/stores/userStore.ts", areas));
        Assert.Equal("API", CodeMapHeuristics.AreaOf("apps/api/Services/UserService.cs", areas));
    }

    [Fact]
    public void Prompt_references_are_paths_and_identifiers()
    {
        var (paths, identifiers) = CodeMapHeuristics.References("ubah src/Web/UserController.cs dan UserBL supaya validasi Description");

        Assert.Equal(["src/Web/UserController.cs"], paths);
        Assert.Contains("UserBL", identifiers);
        Assert.DoesNotContain("Description", identifiers);
    }

    [Fact]
    public void Ranking_narrows_by_module_first_then_weights_rare_keywords()
    {
        var common = Entry("User", MemoryKind.Rule, ["Validation", "Form"]);
        var precise = Entry("User", MemoryKind.Rule, ["Validation", "UserValidator", "Description"]);
        var otherModule = Entry("Invoice", MemoryKind.Rule, ["Description", "UserValidator"]);
        var flow = Entry("User", MemoryKind.Flow, ["Save"]);
        var entries = new[] { common, precise, otherModule, flow };

        var ranked = MemoryRanker.RankLabeled(entries, ["User"], null, null, MemoryLabels.Tokens("UserValidator description"), 10);

        // The whole module stays (layer 1), best keyword match first (layer 2); the other module is out.
        Assert.Equal(precise.Id, ranked[0].Id);
        Assert.Equal(3, ranked.Count);
        Assert.DoesNotContain(ranked, e => e.Id == otherModule.Id);

        var flows = MemoryRanker.RankLabeled(entries, ["User"], null, MemoryKind.Flow, [], 10);
        Assert.Equal(flow.Id, Assert.Single(flows).Id);

        // Without a module only matches count, and matching is per token: "form" does not match "information".
        Assert.Empty(MemoryRanker.RankLabeled(entries, null, null, null, MemoryLabels.Tokens("information"), 10));
    }

    [Fact]
    public void Modules_are_detected_from_their_name_or_alias_in_free_text()
    {
        var modules = new[]
        {
            new ProjectModule { Name = "TransferOrder", Aliases = "TO" },
            new ProjectModule { Name = "User", Aliases = "Pengguna" },
            new ProjectModule { Name = "Core" }
        };

        Assert.Equal(["TransferOrder", "User"], MemoryRanker.DetectModules(modules, MemoryLabels.Tokens("approval transfer order untuk pengguna")));
        Assert.Empty(MemoryRanker.DetectModules(modules, MemoryLabels.Tokens("upgrade EF Core")));
    }

    [Fact]
    public async Task Registry_resolves_names_aliases_plurals_and_merged_modules()
    {
        await using var harness = await OrchestrationHarness.CreateAsync();
        var registry = harness.Service<IModuleRegistry>();

        var (user, created) = await registry.EnsureAsync("k", "user", "Pengguna aplikasi", ["pengguna"]);
        Assert.Equal(("User", true), (user, created));
        Assert.Equal(("User", false), await registry.EnsureAsync("k", "Users"));
        Assert.Equal(("User", false), await registry.EnsureAsync("k", "Pengguna"));
        Assert.Equal("User", await registry.ResolveAsync("k", "users"));
        Assert.Null(await registry.ResolveAsync("other-key", "User"));
        Assert.Contains(registry.DrainCreated()["k"], m => m == "User");
    }

    [Fact]
    public async Task Merging_moves_memory_code_map_and_wiki_and_undo_restores_them()
    {
        await using var harness = await OrchestrationHarness.CreateAsync();
        await SetKeyAsync(harness, "k");
        var registry = harness.Service<IModuleRegistry>();
        await registry.EnsureAsync("k", "User");
        await registry.EnsureAsync("k", "Pengguna");
        var memoryId = await AddLabeledLongAsync(harness, "k", "Pengguna", MemoryKind.Rule, ["Deskripsi"]);
        await using (var catalog = await harness.Service<LongMemoryStore>().CreateCatalogAsync())
        {
            catalog.CodeMapEntries.Add(new CodeMapEntry { ProjectKey = "k", Path = "src/PenggunaBL.cs", Module = "Pengguna", Layer = MemoryLayer.Logic, Source = CodeMapSource.AI });
            await catalog.SaveChangesAsync();
        }
        await harness.Service<IBusinessPageStore>().SaveAsync(new AIThread.Service.Business.BusinessPageDraft(
            null, "k", "Demo", "Pengguna", "Pendaftaran", null, "daftar", "# Pendaftaran\nIsi halaman.", true, null, BusinessEditKind.Manual, "dev", null, []), default);

        var merger = harness.Service<IModuleMerger>();
        var log = await merger.MergeAsync("k", "Pengguna", "User", "translation", 0.95, "dev", default);

        var row = await LongAsync(harness, memoryId);
        Assert.Equal("User", row.Module);
        Assert.StartsWith("User · Rule", row.Label);
        Assert.Equal("User", await registry.ResolveAsync("k", "Pengguna"));
        Assert.Equal("User", (await harness.Service<ICodeMapper>().ListAsync("k", null, null, 10, default)).Single().Module);
        await using (var business = await harness.Service<BusinessMemoryStore>().CreateAsync())
            Assert.Equal("User", (await business.BusinessPages.AsNoTracking().SingleAsync()).Module);
        Assert.Equal(1, ModuleMerger.ToDto(log).Memories);

        await merger.UndoAsync(log.Id, "dev", default);

        Assert.Equal("Pengguna", (await LongAsync(harness, memoryId)).Module);
        Assert.Equal("Pengguna", await registry.ResolveAsync("k", "Pengguna"));
        Assert.Equal("Pengguna", (await harness.Service<ICodeMapper>().ListAsync("k", null, null, 10, default)).Single().Module);
        await using (var business = await harness.Service<BusinessMemoryStore>().CreateAsync())
            Assert.Equal("Pengguna", (await business.BusinessPages.AsNoTracking().SingleAsync()).Module);
    }

    [Fact]
    public async Task Duplicate_check_merges_spellings_at_once_and_follows_the_model_by_confidence()
    {
        await using var harness = await OrchestrationHarness.CreateAsync();
        await harness.AddNodeAsync(NodeRole.ToolsNode, "tools-model");
        var registry = harness.Service<IModuleRegistry>();
        foreach (var name in new[] { "User", "Invoice", "Pengguna", "Tagihan" }) await registry.EnsureAsync("k", name);
        await AddLabeledLongAsync(harness, "k", "User", MemoryKind.Rule, ["Email"]);
        // "Users" cannot be created through the registry (it resolves to User), as if another version wrote it.
        await using (var catalog = await harness.Service<LongMemoryStore>().CreateCatalogAsync())
        {
            catalog.ProjectModules.Add(new ProjectModule { ProjectKey = "k", Name = "Users" });
            await catalog.SaveChangesAsync();
        }
        harness.Router.On(ScriptedAgent.ModuleDuplicates, ScriptedRouter.Text("""
            {"merges": [
              {"from": "Pengguna", "into": "User", "confidence": 0.95, "reason": "translation"},
              {"from": "Tagihan", "into": "Invoice", "confidence": 0.6, "reason": "maybe the same"}
            ]}
            """));

        var found = await harness.Service<IModuleMerger>().CheckAsync("k", null, default);

        Assert.Equal(3, found);
        var active = (await registry.ListAsync("k")).Select(m => m.Name).ToList();
        Assert.Equal(["Invoice", "Tagihan", "User"], active);
        var suggestion = Assert.Single(await harness.Service<IModuleMerger>().ListAsync("k", ModuleMergeStatus.Suggested, 10, default));
        Assert.Equal(("Tagihan", "Invoice"), (suggestion.From, suggestion.Into));

        // A rejected suggestion is not suggested again.
        await harness.Service<IModuleMerger>().RejectAsync(suggestion.Id, "dev", default);
        harness.Router.On(ScriptedAgent.ModuleDuplicates, ScriptedRouter.Text("""{"merges": [{"from": "Tagihan", "into": "Invoice", "confidence": 0.6}]}"""));
        Assert.Equal(0, await harness.Service<IModuleMerger>().CheckAsync("k", null, default));
    }

    [Fact]
    public async Task Search_narrows_by_module_and_keeps_unlabeled_memory_visible()
    {
        await using var harness = await OrchestrationHarness.CreateAsync();
        await SetKeyAsync(harness, "k");
        using var shortDb = new TempWorkspace();
        var memory = CreateMemoryService(harness, shortDb);

        var rule = await memory.AddShortAsync(new SaveMemoryRequest(MemoryType.Project, harness.ProjectId, "user", MemoryKind.Rule,
            ["description", "UserValidator"], "Deskripsi user 10–500 karakter.", [MemoryLayer.Logic]));
        await memory.AddShortAsync(new SaveMemoryRequest(MemoryType.Project, harness.ProjectId, "Invoice", MemoryKind.Rule,
            ["Description"], "Deskripsi invoice wajib."));
        var legacy = await AddLegacyLongAsync(harness, "k", "user, pengguna, description");

        Assert.Equal("User · Logic · Rule", rule.Label);
        Assert.Equal(["Description", "UserValidator"], rule.Keywords);

        var results = await memory.SearchAsync(new MemorySearch(harness.ProjectId, Module: "users", Keywords: ["description"]));
        Assert.Equal(rule.Id, results[0].Id);
        Assert.Contains(results, e => e.Id == legacy && !e.Labeled);
        Assert.DoesNotContain(results, e => e.Module == "Invoice");

        var summary = Assert.Single(await memory.GetModuleSummaryAsync(harness.ProjectId), s => s.Module == "User");
        Assert.Equal(1, summary.Kinds[MemoryKind.Rule]);
        Assert.Equal(1, summary.Layers[MemoryLayer.Logic]);
    }

    [Fact]
    public async Task Memory_of_several_modules_is_found_through_each_of_them()
    {
        await using var harness = await OrchestrationHarness.CreateAsync();
        await SetKeyAsync(harness, "k");
        using var shortDb = new TempWorkspace();
        var memory = CreateMemoryService(harness, shortDb);
        await harness.Service<IModuleRegistry>().EnsureAsync("k", "Invoice", aliases: ["Tagihan"]);

        var flow = await memory.AddShortAsync(new SaveMemoryRequest(MemoryType.Project, harness.ProjectId, "order", MemoryKind.Flow,
            ["Posting", "Invoice"], "Order yang diposting membuat tagihan.", [MemoryLayer.Logic], ExtraModules: ["tagihan", "Orders"]));

        Assert.Equal(["Invoice"], flow.ExtraModules);
        Assert.Equal("Order+Invoice · Logic · Flow", flow.Label);
        Assert.Equal(["Posting"], flow.Keywords);

        var byFilter = await memory.QueryAsync(new MemoryQuery(null, null, harness.ProjectId, null, Module: "Invoice"));
        Assert.Equal(flow.Id, Assert.Single(byFilter.Items).Id);
        var bySearch = await memory.SearchAsync(new MemorySearch(harness.ProjectId, Module: "Invoice"));
        Assert.Contains(bySearch, e => e.Id == flow.Id);
        var summary = await memory.GetModuleSummaryAsync(harness.ProjectId);
        Assert.Equal(["Invoice", "Order"], summary.Select(s => s.Module).Order());

        // Consolidated into long memory, the extra module goes along.
        Assert.True(await memory.PromoteAsync(flow.Id));
        Assert.Equal("Invoice", (await LongAsync(harness, flow.Id)).ExtraModules);
    }

    [Fact]
    public async Task Renaming_a_module_follows_into_memory_code_map_wiki_and_keeps_the_old_name_as_alias()
    {
        await using var harness = await OrchestrationHarness.CreateAsync();
        await SetKeyAsync(harness, "k");
        var registry = harness.Service<IModuleRegistry>();
        await registry.EnsureAsync("k", "klaimdf");
        await registry.EnsureAsync("k", "Order");
        var main = await AddLabeledLongAsync(harness, "k", "Klaimdf", MemoryKind.Rule, ["BtnSave"]);
        var extra = await AddLabeledLongAsync(harness, "k", "Order", MemoryKind.Flow, ["Posting"], ["Klaimdf"]);
        await using (var catalog = await harness.Service<LongMemoryStore>().CreateCatalogAsync())
        {
            catalog.CodeMapEntries.Add(new CodeMapEntry { ProjectKey = "k", Path = "Scripts/klaimdfscript.js", Module = "Klaimdf", Layer = MemoryLayer.UI, Source = CodeMapSource.AI });
            await catalog.SaveChangesAsync();
        }
        await harness.Service<IBusinessPageStore>().SaveAsync(new AIThread.Service.Business.BusinessPageDraft(
            null, "k", "Demo", "Klaimdf", "Klaim DF", null, "klaim", "# Klaim DF\nIsi halaman.", true, null, BusinessEditKind.Manual, "dev", null, []), default);
        var merger = harness.Service<IModuleMerger>();

        // Only the casing.
        Assert.Equal("KlaimDF", await merger.RenameAsync("k", "Klaimdf", "KlaimDF", "dev", default));
        Assert.Equal(("KlaimDF", "KlaimDF · Rule | BtnSave"), ((await LongAsync(harness, main)).Module, (await LongAsync(harness, main)).Label));
        Assert.Equal("KlaimDF", (await LongAsync(harness, extra)).ExtraModules);
        Assert.Equal("KlaimDF", (await harness.Service<ICodeMapper>().ListAsync("k", null, null, 10, default)).Single().Module);
        await using (var business = await harness.Service<BusinessMemoryStore>().CreateAsync())
            Assert.Equal("KlaimDF", (await business.BusinessPages.AsNoTracking().SingleAsync()).Module);
        Assert.Null((await registry.ListAsync("k")).Single(m => m.Name == "KlaimDF").Aliases);

        // Another name: the old one stays an alias; a name another module has is refused.
        Assert.Equal("ClaimDF", await merger.RenameAsync("k", "KlaimDF", "ClaimDF", "dev", default));
        Assert.Equal("ClaimDF", await registry.ResolveAsync("k", "klaimdf"));
        Assert.Equal("Order+ClaimDF · Flow | Posting", (await LongAsync(harness, extra)).Label);
        await Assert.ThrowsAsync<AppValidationException>(() => merger.RenameAsync("k", "ClaimDF", "orders", "dev", default));
    }

    [Fact]
    public async Task Saving_requires_a_full_label()
    {
        await using var harness = await OrchestrationHarness.CreateAsync();
        using var shortDb = new TempWorkspace();
        var memory = CreateMemoryService(harness, shortDb);

        await Assert.ThrowsAsync<AppValidationException>(() => memory.AddShortAsync(
            new SaveMemoryRequest(MemoryType.Knowledge, null, "Caching", MemoryKind.Rule, ["Ttl"], "Business rules are never knowledge.")));
        await Assert.ThrowsAsync<AppValidationException>(() => memory.AddShortAsync(
            new SaveMemoryRequest(MemoryType.Project, harness.ProjectId, "User", MemoryKind.Rule, [], "No keywords.")));
        await Assert.ThrowsAsync<AppValidationException>(() => memory.AddShortAsync(
            new SaveMemoryRequest(MemoryType.Project, harness.ProjectId, " ", MemoryKind.Rule, ["Email"], "No module.")));
    }

    [Fact]
    public async Task Code_map_asks_the_model_once_per_stem_and_keeps_manual_entries()
    {
        await using var harness = await OrchestrationHarness.CreateAsync();
        await SetKeyAsync(harness, "k");
        await harness.AddNodeAsync(NodeRole.ToolsNode, "tools-model");
        harness.Project.Write("src/Web/Controllers/UserController.cs", "class UserController {}");
        harness.Project.Write("src/Bl/UserBL.cs", "class UserBL {}");
        harness.Router.On(ScriptedAgent.CodeMapper, call => ScriptedRouter.Text("""
            {"groups": [{"id": "G1", "module": "UserAccount", "description": "Akun pengguna", "aliases": ["Pengguna"]}]}
            """));
        var project = (await harness.Service<IProjectService>().GetContextAsync(harness.ProjectId))!;
        var mapper = harness.Service<ICodeMapper>();

        var hits = await mapper.MapAsync(project, ["src/Web/Controllers/UserController.cs", "src/Bl/UserBL.cs"], default);

        Assert.All(hits, h => Assert.Equal("UserAccount", h.Module));
        Assert.Equal([MemoryLayer.Entry, MemoryLayer.Logic], hits.OrderBy(h => h.Path).Select(h => h.Layer).Reverse());
        Assert.Single(harness.Router.CallsTo(ScriptedAgent.CodeMapper));

        // Mapped files are answered from the map; a manual decision is never overwritten.
        await mapper.SetManualAsync(project, "src/Bl/UserBL.cs", "Account", null, default);
        var again = await mapper.MapAsync(project, ["src/Web/Controllers/UserController.cs", "src/Bl/UserBL.cs"], default);
        Assert.Single(harness.Router.CallsTo(ScriptedAgent.CodeMapper));
        Assert.Equal("Account", again.Single(h => h.Path.EndsWith("UserBL.cs")).Module);
        Assert.Equal(CodeMapSource.Manual, again.Single(h => h.Path.EndsWith("UserBL.cs")).Source);

        Assert.Equal(["src/Bl/UserBL.cs"], await mapper.ResolveReferencesAsync(project, "pindahkan validasi dari UserBL", default));
    }

    [Fact]
    public async Task Code_map_scan_reports_progress_and_resumes_where_it_stopped()
    {
        await using var harness = await OrchestrationHarness.CreateAsync();
        await SetKeyAsync(harness, "k");
        await harness.AddNodeAsync(NodeRole.ToolsNode, "tools-model");
        harness.Project.Write("src/Bl/OrderBL.cs", "class OrderBL {}");
        harness.Project.Write("src/Bl/InvoiceBL.cs", "class InvoiceBL {}");
        harness.Router.On(ScriptedAgent.CodeMapper, call => ScriptedRouter.Text("""{"groups": [{"id": "G1", "module": "Sales"}]}"""));
        var project = (await harness.Service<IProjectService>().GetContextAsync(harness.ProjectId))!;
        var mapper = harness.Service<ICodeMapper>();

        // A scan cut off after the first file: only the rest goes to the model.
        await mapper.MapAsync(project, ["src/Bl/OrderBL.cs"], default);
        var progress = new List<(int Done, int Total)>();
        var (files, mapped) = await mapper.ScanAsync(project, 3_000, default, (done, total) => progress.Add((done, total)));

        var calls = harness.Router.CallsTo(ScriptedAgent.CodeMapper);
        Assert.Equal(2, calls.Count);
        Assert.Contains("InvoiceBL.cs", calls[1].LastUser);
        Assert.DoesNotContain("OrderBL.cs", calls[1].LastUser);
        Assert.Equal(files, mapped);
        Assert.True(progress[0].Done < files);
        Assert.Equal((files, files), progress[^1]);

        // A finished scan asks the model nothing.
        progress.Clear();
        await mapper.ScanAsync(project, 3_000, default, (done, total) => progress.Add((done, total)));
        Assert.Equal(2, harness.Router.CallsTo(ScriptedAgent.CodeMapper).Count);
        Assert.Equal([(files, files)], progress);
    }

    [Fact]
    public async Task Code_map_of_memory_maps_only_the_files_it_names()
    {
        await using var harness = await OrchestrationHarness.CreateAsync();
        await SetKeyAsync(harness, "k");
        await harness.AddNodeAsync(NodeRole.ToolsNode, "tools-model");
        harness.Project.Write("src/Bl/UserBL.cs", "class UserBL {}");
        harness.Project.Write("src/Web/Controllers/OrderController.cs", "class OrderController {}");
        harness.Project.Write("src/Bl/InvoiceBL.cs", "class InvoiceBL {}");
        var project = (await harness.Service<IProjectService>().GetContextAsync(harness.ProjectId))!;
        var mapper = harness.Service<ICodeMapper>();

        var (files, mapped) = await mapper.MapReferencedAsync(project,
            ["Validasi email ada di UserBL.", "Lihat Controllers/OrderController.cs untuk status.", "Tanpa referensi file."], default);

        Assert.Equal((2, 2), (files, mapped));
        var stored = await mapper.StoredAsync("k", ["src/Bl/UserBL.cs", "src/Web/Controllers/OrderController.cs", "src/Bl/InvoiceBL.cs"], default);
        Assert.Equal(["src/Bl/UserBL.cs", "src/Web/Controllers/OrderController.cs"], stored.Keys.Order());
    }

    [Fact]
    public async Task Code_map_batches_run_in_parallel_and_map_every_file()
    {
        await using var harness = await OrchestrationHarness.CreateAsync();
        await SetKeyAsync(harness, "k");
        await harness.AddNodeAsync(NodeRole.ToolsNode, "tools-model");
        var paths = Enumerable.Range(1, 90).Select(i => $"src/Bl/Feature{(char)('A' + i / 26)}{(char)('a' + i % 26)}BL.cs").ToList();
        foreach (var path in paths) harness.Project.Write(path, "class X {}");
        var project = (await harness.Service<IProjectService>().GetContextAsync(harness.ProjectId))!;
        var mapper = harness.Service<ICodeMapper>();
        var progress = new List<int>();

        await mapper.ScanAsync(project, 3_000, default, (done, _) => { lock (progress) progress.Add(done); });

        Assert.Equal(3, harness.Router.CallsTo(ScriptedAgent.CodeMapper).Count);
        Assert.Equal(paths.Count, (await mapper.StoredAsync("k", paths, default)).Count);
        Assert.Equal(progress.Order(), progress);
    }

    [Fact]
    public async Task Relabeling_backs_up_then_labels_or_queues_for_review()
    {
        await using var harness = await OrchestrationHarness.CreateAsync();
        await SetKeyAsync(harness, "k");
        await harness.AddNodeAsync(NodeRole.ToolsNode, "tools-model");
        using var backup = new TempWorkspace();
        MemoryRelabeler.BackupDirectory = backup.Root;
        var clear = await AddLegacyLongAsync(harness, "k", "user, description, validation");
        var vague = await AddLegacyLongAsync(harness, "k", "misc");
        harness.Router.On(ScriptedAgent.MemoryRelabeler, call =>
        {
            Assert.Contains("old label: user, description, validation", call.LastUser);
            return ScriptedRouter.Text("""
                {"memories": [
                  {"id": "M1", "module": "user", "layers": ["Logic"], "kind": "validation", "keywords": ["description", "deskripsi"]},
                  {"id": "M2", "review": "too vague"}
                ]}
                """);
        });

        await harness.Service<IMemoryRelabeler>().RunAsync(false, default);

        var labeled = await LongAsync(harness, clear);
        Assert.Equal((MemoryLabels.CurrentVersion, "User", MemoryKind.Rule, "Logic"), (labeled.LabelVersion, labeled.Module, labeled.Kind, labeled.Layers));
        Assert.Equal("Description,Deskripsi", labeled.Keywords);
        Assert.Equal("user, description, validation", labeled.LegacyLabel);
        Assert.Equal("User · Logic · Rule | Description, Deskripsi", labeled.Label);
        Assert.Equal("too vague", (await LongAsync(harness, vague)).ReviewReason);
        Assert.Single(Directory.GetFiles(backup.Root, "memory-before-labels-v1-*.json"));

        var status = await harness.Service<IMemoryRelabeler>().GetStatusAsync(default);
        Assert.Equal((0, 1, 1), (status.Pending, status.Labeled, status.NeedsReview));
    }

    [Fact]
    public async Task Relabeling_gives_up_after_three_failed_attempts()
    {
        await using var harness = await OrchestrationHarness.CreateAsync();
        await SetKeyAsync(harness, "k");
        await harness.AddNodeAsync(NodeRole.ToolsNode, "tools-model");
        using var backup = new TempWorkspace();
        MemoryRelabeler.BackupDirectory = backup.Root;
        var id = await AddLegacyLongAsync(harness, "k", "user");

        // The default scripted answer labels nothing.
        for (var i = 0; i < MemoryRelabeler.MaxAttempts; i++) await harness.Service<IMemoryRelabeler>().RunAsync(false, default);

        var row = await LongAsync(harness, id);
        Assert.Null(row.LabelVersion);
        Assert.Equal(MemoryRelabeler.MaxAttempts, row.RelabelAttempts);
        Assert.StartsWith("Gagal dilabeli otomatis", row.ReviewReason);
    }

    private static MemoryIndexEntry Entry(string module, MemoryKind kind, IReadOnlyList<string> keywords) =>
        new(Guid.NewGuid(), MemoryLevel.Long, MemoryType.Project, module, DateTime.UtcNow, module, [MemoryLayer.Logic], kind, null, keywords);

    private static MemoryService CreateMemoryService(OrchestrationHarness harness, TempWorkspace shortDb)
    {
        var redis = new RedisConnectionProvider("127.0.0.1:1", NullLogger<RedisConnectionProvider>.Instance);
        var shortStore = new SwitchableShortMemoryStore(redis, "127.0.0.1:1", NullLoggerFactory.Instance);
        shortStore.Configure(new MemoryStoreConnection(MemoryStoreProvider.Sqlite, Database: Path.Combine(shortDb.Root, "short.db"), Options: "Pooling=False"));
        return new MemoryService(
            harness.Service<IDbContextFactory<AIThreadDbContext>>(),
            shortStore,
            harness.Service<LongMemoryStore>(),
            harness.Service<BusinessMemoryStore>(),
            harness.Service<IModuleRegistry>(),
            harness.Service<IEventBus>(),
            harness.Service<IActivityLogService>(),
            Options.Create(new AIThreadOptions()),
            NullLogger<MemoryService>.Instance);
    }

    private static async Task SetKeyAsync(OrchestrationHarness harness, string key)
    {
        await using var db = await harness.CreateDbAsync();
        await db.Projects.Where(p => p.Id == harness.ProjectId).ExecuteUpdateAsync(s => s.SetProperty(p => p.ProjectKey, key));
    }

    private static async Task<Guid> AddLegacyLongAsync(OrchestrationHarness harness, string key, string label)
    {
        await using var db = await harness.CreateDbAsync();
        var row = new LongMemory { Id = Guid.NewGuid(), ProjectId = harness.ProjectId, ProjectKey = key, Type = MemoryType.Project, Label = label, Description = $"Memory about {label}." };
        db.LongMemories.Add(row);
        await db.SaveChangesAsync();
        return row.Id;
    }

    private static async Task<Guid> AddLabeledLongAsync(OrchestrationHarness harness, string key, string module, MemoryKind kind, IReadOnlyList<string> keywords,
        IReadOnlyList<string>? extraModules = null)
    {
        await using var db = await harness.CreateDbAsync();
        var row = new LongMemory
        {
            Id = Guid.NewGuid(), ProjectId = harness.ProjectId, ProjectKey = key, Type = MemoryType.Project, Description = "d",
            Module = module, ExtraModules = MemoryLabels.JoinCsv(extraModules), Kind = kind, Keywords = MemoryLabels.JoinCsv(keywords),
            LabelVersion = MemoryLabels.CurrentVersion, Label = MemoryLabels.Compose(MemoryLabels.Display(null, module, [], kind, extraModules), keywords)
        };
        db.LongMemories.Add(row);
        await db.SaveChangesAsync();
        return row.Id;
    }

    private static async Task<LongMemory> LongAsync(OrchestrationHarness harness, Guid id)
    {
        await using var db = await harness.CreateDbAsync();
        return await db.LongMemories.AsNoTracking().SingleAsync(m => m.Id == id);
    }
}

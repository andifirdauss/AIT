using AIThread.Common.DTOs;
using AIThread.Common.Entities;
using AIThread.Service.Interfaces;
using AIThread.Service.Models;
using AIThread.Service.Testing;

namespace AIThread.Tests;

// The test accounts of a project (Project > Detail → Akun uji): kept per project key, unique usernames whatever their
// case, free attributes, the development password, and their trip through export/import memory.
public class TestAccountTests
{
    private const string Key = "github.com/org/shop";

    [Fact]
    public async Task Accounts_are_kept_per_project_key_with_unique_usernames_and_free_attributes()
    {
        await using var harness = await OrchestrationHarness.CreateAsync();
        var service = harness.Service<ITestAccountService>();

        var withoutKey = await service.GetAsync(harness.ProjectId);
        Assert.False(withoutKey.CanSave);
        Assert.Contains("project key", withoutKey.Problem);
        await Assert.ThrowsAsync<AppValidationException>(() => service.AddAsync(harness.ProjectId, new SaveTestAccountRequest("andi", null, null, null)));

        await harness.Service<IProjectService>().SetKeyAsync(harness.ProjectId, Key);
        var andi = await service.AddAsync(harness.ProjectId, new SaveTestAccountRequest(" andi ", " Pengaju ", new() { ["NIK"] = " 123 ", ["Kosong"] = " ", ["Unit"] = "Sales" }, null));
        Assert.Equal(("andi", "Pengaju"), (andi.Username, andi.Role));
        Assert.Equal(new Dictionary<string, string> { ["NIK"] = "123", ["Unit"] = "Sales" }, andi.Attributes);

        var duplicate = await Assert.ThrowsAsync<AppValidationException>(() => service.AddAsync(harness.ProjectId, new SaveTestAccountRequest("ANDI", null, null, null)));
        Assert.Contains("sudah ada", duplicate.Message);
        await Assert.ThrowsAsync<AppValidationException>(() => service.AddAsync(harness.ProjectId, new SaveTestAccountRequest("andi budi", null, null, null)));

        await service.AddAsync(harness.ProjectId, new SaveTestAccountRequest("budi", "Atasan", new() { ["Unit"] = "Sales", ["Level approval"] = "1" }, "untuk flow cuti"));
        var updated = await service.UpdateAsync(harness.ProjectId, andi.Id, new SaveTestAccountRequest("andi", "Pengaju", new() { ["Atasan"] = "budi" }, null));
        Assert.Equal("budi", updated.Attributes["Atasan"]);

        var list = await service.GetAsync(harness.ProjectId);
        Assert.True(list.CanSave);
        Assert.Equal(["andi", "budi"], list.Accounts.Select(a => a.Username));
        // Every key once here, so in name order; a key more accounts use would come first.
        Assert.Equal(["Atasan", "Level approval", "Unit"], list.AttributeKeys);
        Assert.True(list.Settings.DevPasswordIsDefault);
        Assert.Equal(TestingSettings.DefaultDevPassword, list.Settings.DevPassword);

        await service.DeleteAsync(harness.ProjectId, andi.Id);
        Assert.Equal(["budi"], (await service.GetAsync(harness.ProjectId)).Accounts.Select(a => a.Username));
    }

    [Fact]
    public async Task The_development_password_falls_back_to_the_default_when_cleared()
    {
        await using var harness = await OrchestrationHarness.CreateAsync();
        await harness.Service<IProjectService>().SetKeyAsync(harness.ProjectId, Key);
        var service = harness.Service<ITestAccountService>();

        var saved = await service.SaveSettingsAsync(harness.ProjectId, new SaveTestingSettingsRequest(" 123 "));
        Assert.Equal(("123", false, TestingSettings.SourceDeveloper), (saved.DevPassword, saved.DevPasswordIsDefault, saved.DevPasswordSource));

        var cleared = await service.SaveSettingsAsync(harness.ProjectId, new SaveTestingSettingsRequest(""));
        Assert.Equal((TestingSettings.DefaultDevPassword, true, TestingSettings.SourceDefault), (cleared.DevPassword, cleared.DevPasswordIsDefault, cleared.DevPasswordSource));
    }

    [Fact]
    public async Task Accounts_and_settings_travel_through_export_and_import_and_the_newer_copy_wins()
    {
        MemoryExportFile file;
        await using (var source = await OrchestrationHarness.CreateAsync())
        {
            await source.Service<IProjectService>().SetKeyAsync(source.ProjectId, Key);
            var service = source.Service<ITestAccountService>();
            await service.AddAsync(source.ProjectId, new SaveTestAccountRequest("andi", "Pengaju", new() { ["NIK"] = "123" }, null));
            await service.SaveSettingsAsync(source.ProjectId, new SaveTestingSettingsRequest("Dev#1"));
            file = await source.Service<IMemoryTransferService>().ExportAsync(new MemoryQuery(null, null, null, null));
        }

        Assert.Equal(4, file.Version);
        Assert.Equal("andi", Assert.Single(file.Testing!.Accounts).Username);

        await using var target = await OrchestrationHarness.CreateAsync();
        await target.Service<IProjectService>().SetKeyAsync(target.ProjectId, Key);
        var store = target.Service<ITestAccountStore>();
        // An older local copy of the same username is replaced, an unrelated account stays.
        await store.AddAsync(Key, new SaveTestAccountRequest("ANDI", "Lama", null, null), "lokal");
        await store.AddAsync(Key, new SaveTestAccountRequest("citra", "HR", null, null), "lokal");
        var transfer = target.Service<IMemoryTransferService>();

        var preview = await transfer.ImportAsync(file, MemoryImportMode.Skip, dryRun: true);
        Assert.Equal((0, 0), (preview.TestAccountsAdded, preview.TestAccountsUpdated));
        Assert.Equal(1, preview.TestAccountsSkipped);

        // The file's copy was written before the local one: the local one stays.
        var set = await store.GetAsync(Key);
        Assert.Equal("Lama", set.Find("andi")!.Role);

        // A newer copy in the file wins.
        var newer = file with
        {
            Testing = file.Testing with
            {
                Accounts = [file.Testing.Accounts[0] with { UpdatedAt = DateTime.UtcNow.AddMinutes(5) }],
                Settings = [file.Testing.Settings[0] with { UpdatedAt = DateTime.UtcNow.AddMinutes(5) }]
            }
        };
        var result = await transfer.ImportAsync(newer, MemoryImportMode.Skip, dryRun: false);
        Assert.Equal(1, result.TestAccountsUpdated);
        set = await store.GetAsync(Key);
        Assert.Equal(("andi", "Pengaju", "123"), (set.Find("andi")!.Username, set.Find("andi")!.Role, TestAccountText.ParseAttributes(set.Find("andi")!.Attributes)["NIK"]));
        Assert.NotNull(set.Find("citra"));
        Assert.Equal("Dev#1", set.Settings.DevPassword);

        // Importing the same file again changes nothing.
        Assert.Equal((0, 0), ((await transfer.ImportAsync(newer, MemoryImportMode.Skip, dryRun: false)).TestAccountsAdded, (await transfer.ImportAsync(newer, MemoryImportMode.Skip, dryRun: false)).TestAccountsUpdated));
    }

    private const string LoginAsController = """
        public class AccountController : Controller
        {
            [Authorize(Roles = "Administrator")]
            [HttpGet("/Account/LoginAs")]
            public async Task<IActionResult> LoginAs(string username) => await SignInAsAsync(username);
        }
        """;

    [Fact]
    public void The_scanner_finds_login_as_in_the_projects_own_code_only()
    {
        using var workspace = new TempWorkspace();
        workspace.Write("Controllers/AccountController.cs", LoginAsController);
        workspace.Write("Controllers/HomeController.cs", "public class HomeController { public IActionResult Index() => View(); }");
        workspace.Write("bin/Debug/Generated.cs", "class Generated { void LoginAs() {} }");
        workspace.Write("node_modules/lib/index.js", "export function impersonate() {}");
        workspace.Write("README.md", "Admin dapat login sebagai user lain.");

        var hits = ImpersonationScanner.Scan(new Service.Workspace.WorkspaceScope(workspace.Root), CancellationToken.None);

        var hit = Assert.Single(hits);
        Assert.Equal("Controllers/AccountController.cs", hit.File);
        Assert.Contains("Administrator", hit.Excerpt);
    }

    [Fact]
    public async Task Detection_asks_the_cheap_model_only_when_the_code_has_hits_and_keeps_the_result_unconfirmed()
    {
        await using var harness = await OrchestrationHarness.CreateAsync();
        await harness.AddStandardNodesAsync();
        await harness.Service<IProjectService>().SetKeyAsync(harness.ProjectId, Key);
        var service = harness.Service<ITestAccountService>();

        var none = (await service.DetectImpersonationAsync(harness.ProjectId)).Impersonation!;
        Assert.Equal((ImpersonationInfo.None, ImpersonationInfo.FromScan), (none.Status, none.Source));
        Assert.Empty(harness.Router.CallsTo(ScriptedAgent.ImpersonationDetector));

        harness.Project.Write("Controllers/AccountController.cs", LoginAsController);
        harness.Router.On(ScriptedAgent.ImpersonationDetector, call =>
        {
            Assert.Contains("Controllers/AccountController.cs (line", call.LastUser);
            return ScriptedRouter.Text("""{"available":true,"how":"GET /Account/LoginAs?username={{target}}","back":"","roles":["Administrator"],"evidence":["Controllers/AccountController.cs:4"]}""");
        });

        var found = (await service.DetectImpersonationAsync(harness.ProjectId)).Impersonation!;
        Assert.Equal((ImpersonationInfo.Available, "GET /Account/LoginAs?username={{target}}", false), (found.Status, found.How, found.Confirmed));
        Assert.Equal(["Administrator"], found.Roles);
        Assert.Null(found.Back);

        // The developer confirms it; a later observation by the tester does not replace it.
        await service.SaveImpersonationAsync(harness.ProjectId, new SaveImpersonationRequest("available", found.How, null, ["Administrator"], true));
        var seen = found with { How = "Admin › Users › Login as", Source = ImpersonationInfo.FromTester };
        Assert.False(await harness.Service<IImpersonationDetector>().ObserveAsync(Key, seen, "AIT Tester"));
        var kept = (await service.GetAsync(harness.ProjectId)).Settings.Impersonation!;
        Assert.Equal((true, ImpersonationInfo.FromDeveloper), (kept.Confirmed, kept.Source));

        await Assert.ThrowsAsync<AppValidationException>(() => service.SaveImpersonationAsync(harness.ProjectId, new SaveImpersonationRequest("available", null, null, ["Administrator"], true)));
    }

    [Fact]
    public void The_brief_lists_accounts_without_the_password()
    {
        var settings = new TestingSettings { ProjectKey = Key, DevPassword = "Rahasia#1" };
        var set = new TestAccountSet(
            [new TestAccount { ProjectKey = Key, Username = "andi", Role = "Pengaju", Attributes = """{"NIK":"123","Atasan":"budi"}""" },
             new TestAccount { ProjectKey = Key, Username = "budi", Role = "Atasan", Notes = "level 1" }], settings);

        var tester = TestAccountText.Brief(set, forTester: true)!;
        var planner = TestAccountText.Brief(set, forTester: false)!;

        Assert.Contains("- andi · Pengaju · NIK 123, Atasan budi", tester);
        Assert.Contains("- budi · Atasan — level 1", tester);
        Assert.Contains("switch_user", tester);
        Assert.DoesNotContain("Rahasia#1", tester);
        Assert.DoesNotContain("switch_user", planner);
        Assert.Contains("\"actor\"", planner);
        Assert.Null(TestAccountText.Brief(new TestAccountSet([], settings), forTester: true));
        Assert.Equal(["Pengaju", "Atasan"], TestAccountText.Roles(set));
    }
}

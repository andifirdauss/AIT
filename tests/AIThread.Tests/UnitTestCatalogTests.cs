using AIThread.Common.DTOs;
using AIThread.Common.Entities;
using AIThread.Common.Enums;
using AIThread.Data.Memory;
using AIThread.Service.Interfaces;
using AIThread.Service.Models;
using AIThread.Service.Orchestration;
using AIThread.Service.UnitTests;
using AIThread.Service.Verification;
using AIThread.Service.Workspace;
using Microsoft.EntityFrameworkCore;
using static AIThread.Tests.ThreadOrchestratorTests;

namespace AIThread.Tests;

// The unit test catalog: the planner's decisions about tests, fix tasks for failing tests, numbering, the automatic
// restore of tests a repository lacks, and the catalog in the memory export.
public class UnitTestCatalogTests
{
    internal const string TestProject = "tests/Shop.Tests/Shop.Tests.csproj";
    internal const string TestFile = "tests/Shop.Tests/UserTests.cs";
    internal const string UserFile = "src/User.cs";

    internal const string Tests = """
        namespace Shop.Tests;

        public class UserTests
        {
            [Fact]
            public void Save_WithoutName_IsRejected()
            {
                Assert.False(User.IsValid(""));
            }

            [Fact]
            public void Save_WithName_IsAccepted()
            {
                Assert.True(User.IsValid("Andi"));
            }
        }
        """;

    // A .NET solution with one xunit test project, as discovery and the runner see it.
    internal static void WriteProject(TempWorkspace project)
    {
        project.Write("Shop.slnx", $"""<Solution><Project Path="src/Shop.csproj" /><Project Path="{TestProject}" /></Solution>""");
        project.Write("src/Shop.csproj", """<Project Sdk="Microsoft.NET.Sdk"></Project>""");
        project.Write(UserFile, "public static class User { public static bool IsValid(string name) => name.Length > 0; }");
        project.Write(TestProject, """<Project Sdk="Microsoft.NET.Sdk"><ItemGroup><PackageReference Include="xunit" Version="2.9.3" /></ItemGroup></Project>""");
        project.Write(TestFile, Tests);
    }

    // The project of a harness with the test project above and a project key (the catalog is keyed by it).
    internal static async Task<ProjectContext> PrepareAsync(OrchestrationHarness harness, string key = "github.com/org/shop")
    {
        WriteProject(harness.Project);
        await harness.Service<IProjectService>().SetKeyAsync(harness.ProjectId, key);
        return (await harness.Service<IProjectService>().GetContextAsync(harness.ProjectId))!;
    }

    [Fact]
    public void Plan_decisions_about_tests_and_new_tests_are_validated()
    {
        using var workspace = new TempWorkspace();
        workspace.Write(UserFile, "class User {}");
        workspace.Write(TestFile, Tests);
        workspace.Write("tests/Shop.Tests/OrderTests.cs", "class OrderTests {}");
        var scope = new WorkspaceScope(workspace.Root);
        var rules = new UnitTestRules(new Dictionary<string, string> { ["UT-0001"] = TestFile, ["UT-0002"] = "tests/Shop.Tests/OrderTests.cs" }, AllowNew: true);
        ThreadPlan Plan(PlanTests tests, params PlanTask[] tasks) => new() { Summary = "s", Tasks = tasks.Length == 0 ? [Step("T1", UserFile, "x")] : [.. tasks], Tests = tests };
        IReadOnlyList<string> Errors(ThreadPlan plan, UnitTestRules? with = null) => PlanValidator.Validate(plan, scope, unitTests: with ?? rules);

        var errors = Errors(Plan(new PlanTests { Decisions = [new PlanTestDecision { Test = "ut-1", Decision = "Update" }, new PlanTestDecision { Test = "UT-0099", Decision = "keep" }] }));
        Assert.Contains(errors, e => e.Contains("update of UT-0001 needs \"reason\""));
        Assert.Contains(errors, e => e.Contains("needs an edit task for its test file"));
        Assert.Contains(errors, e => e.Contains("'UT-0099' is not an active test"));

        var newTest = new PlanNewTest { Title = "Nama kosong ditolak", Scenario = "given empty name, when saved, then rejected", File = TestFile, Name = "Save_Empty" };
        Assert.Contains(Errors(Plan(new PlanTests { New = [newTest] })), e => e.Contains("no create or edit task targets that file"));
        Assert.Contains(Errors(Plan(new PlanTests { New = [newTest] }, Step("T1", UserFile, "x"), Step("T2", TestFile, "add test")), rules with { AllowNew = false }),
            e => e.Contains("new tests are turned off"));

        var valid = Plan(new PlanTests
        {
            Decisions =
            [
                new PlanTestDecision { Test = "ut-1", Decision = "update", Reason = "Permintaan: nama boleh kosong." },
                new PlanTestDecision { Test = "UT-0002", Decision = "obsolete", Reason = "Fitur order dihapus." }
            ],
            New = [new PlanNewTest { Title = newTest.Title, Scenario = newTest.Scenario, File = newTest.File, Name = newTest.Name, Requires = ["database", "printer"] }],
            Run = ["all"]
        }, Step("T1", UserFile, "x"), Step("T2", TestFile, "update and add tests"));
        Assert.Empty(Errors(valid));
        Assert.Equal("UT-0001", valid.Tests!.Decisions[0].Test);
        Assert.Equal(["database"], valid.Tests.New[0].Requires);
        Assert.Empty(valid.Tests.Run);

        var keepOnly = Plan(new PlanTests { Decisions = [new PlanTestDecision { Test = "UT-0001", Decision = "keep" }] });
        Assert.Empty(Errors(keepOnly));
        Assert.Null(keepOnly.Tests);

        var withoutCatalog = Plan(new PlanTests { New = [newTest] });
        Assert.Empty(PlanValidator.Validate(withoutCatalog, scope));
        Assert.Null(withoutCatalog.Tests);

        // No tasks: only the tests to run count.
        var run = new ThreadPlan { Summary = "s", Answer = "a", Tests = new PlanTests { Run = ["ut-2", "UT-0001"], Decisions = [new PlanTestDecision { Test = "UT-0001", Decision = "obsolete" }] } };
        Assert.Empty(Errors(run));
        Assert.Equal(["UT-0002", "UT-0001"], run.Tests!.Run);
        Assert.Empty(run.Tests.Decisions);
        Assert.Contains(Errors(new ThreadPlan { Summary = "s", Tests = new PlanTests { Run = ["UT-0099"] } }), e => e.Contains("tests.run: 'UT-0099'"));
        var all = new ThreadPlan { Summary = "s", Tests = new PlanTests { Run = ["UT-0001", "all"] } };
        Assert.Empty(Errors(all));
        Assert.Equal([PlanTests.All], all.Tests!.Run);
    }

    [Fact]
    public void Failing_unit_tests_become_fix_tasks_on_their_file()
    {
        using var workspace = new TempWorkspace();
        workspace.Write(UserFile, "public static class User { public static bool IsValid(string name) => true; }");
        var scope = new WorkspaceScope(workspace.Root);
        var issue = new ReviewIssue(UserFile, "public static bool IsValid(string name) => true;", "Unit test UT-0001 \"Nama kosong ditolak\" passed before this change and fails now",
            "Change the code so this test passes again; do not change the test.");

        var fix = FixTaskFactory.Create(1, null, null, [UserFile], [Step("T1", UserFile, "Allow any name.")], scope,
            [issue, issue with { File = "src/Missing.cs" }]);

        var task = Assert.Single(fix.Tasks);
        Assert.Equal(("V1-1", "Fix failing unit tests", UserFile), (task.Id, task.Title, task.File));
        Assert.Contains("Unit tests that fail after the change", task.Instruction);
        Assert.Contains("UT-0001", task.Instruction);
        Assert.Contains(fix.Unfixable, u => u.Contains("src/Missing.cs") && u.Contains("does not exist"));
    }

    [Fact]
    public async Task Numbers_count_per_project_and_a_rollback_takes_back_what_its_thread_did()
    {
        await using var harness = await OrchestrationHarness.CreateAsync();
        var catalog = harness.Service<IUnitTestCatalog>();
        var thread = Guid.NewGuid();
        UnitTestCase Test(string name, Guid? createdBy = null) => new()
        {
            Title = name, Framework = TestSuite.XUnit, TestFile = TestFile, MethodName = name, Selector = $"Shop.Tests.UserTests.{name}",
            CreatedThreadId = createdBy, Origin = createdBy is null ? UnitTestOrigin.Scanned : UnitTestOrigin.Ait
        };

        var first = await catalog.AddAsync("key-a", [Test("A"), Test("B")], CancellationToken.None);
        var other = await catalog.AddAsync("key-b", [Test("A")], CancellationToken.None);
        var added = await catalog.AddAsync("key-a", [Test("C", thread)], CancellationToken.None);
        Assert.Equal(["UT-0001", "UT-0002"], first.Select(t => t.DisplayNumber));
        Assert.Equal("UT-0001", Assert.Single(other).DisplayNumber);
        Assert.Equal("UT-0003", Assert.Single(added).DisplayNumber);

        var retired = first[1];
        retired.State = UnitTestState.Obsolete;
        retired.ObsoleteReason = "Fitur dihapus.";
        retired.ObsoleteThreadId = thread;
        await catalog.UpdateAsync([retired], CancellationToken.None);
        await catalog.RecordRunsAsync(Enumerable.Range(0, UnitTestCatalog.MaxRunsPerTest + 5)
            .Select(i => new UnitTestRun { UnitTestCaseId = first[0].Id, ProjectKey = "key-a", Phase = UnitTestRun.Manual, Result = UnitTestResult.Passed, CreatedAt = DateTime.UtcNow.AddMinutes(-i) })
            .ToList(), CancellationToken.None);
        Assert.Equal(UnitTestCatalog.MaxRunsPerTest, (await catalog.HistoryAsync(first[0].Id, 100, CancellationToken.None)).Count);

        Assert.Equal(2, await catalog.RevertThreadAsync(thread, CancellationToken.None));
        var left = await catalog.ListAsync("key-a", CancellationToken.None);
        Assert.Equal(["A", "B"], left.Select(t => t.MethodName));
        Assert.Equal((UnitTestState.Active, (string?)null), (left[1].State, left[1].ObsoleteReason));
    }

    [Fact]
    public async Task Missing_tests_come_back_from_their_snapshot_and_moved_tests_follow_their_file()
    {
        await using var harness = await OrchestrationHarness.CreateAsync();
        var project = await PrepareAsync(harness);
        var scan = await harness.Service<IUnitTestScanner>().ScanAsync(project, label: false, "dev", CancellationToken.None);
        Assert.Equal((2, 2), (scan.Found, scan.Added));
        var catalog = harness.Service<IUnitTestCatalog>();
        var tests = await catalog.ListAsync(project.Key, CancellationToken.None);
        Assert.All(tests, t => Assert.NotNull(t.SourceSnapshot));
        Assert.Equal("Save without name is rejected", tests[0].Title);
        Assert.Equal([UserFile], UnitTestText.Lines(tests[0].TargetFiles));
        var suites = TestCommandResolver.Suites(project.RootPath, project.Modules);
        var restorer = harness.Service<IUnitTestRestorer>();

        // The whole file is gone: written back as it was.
        File.Delete(Path.Combine(harness.Project.Root, TestFile));
        var restored = await restorer.RestoreAsync(project, tests, suites, CancellationToken.None);
        Assert.Equal([UnitTestRestoreItem.Snapshot, UnitTestRestoreItem.Snapshot], restored.Items.Select(i => i.How));
        Assert.Empty(restored.Tasks);
        Assert.Equal(Tests.ReplaceLineEndings("\n"), harness.ReadFile(TestFile));

        // One method is gone: it goes back into the class without a model.
        var withoutOne = Tests.Replace("""
                [Fact]
                public void Save_WithName_IsAccepted()
                {
                    Assert.True(User.IsValid("Andi"));
                }
            """, string.Empty);
        harness.Project.Write(TestFile, withoutOne);
        restored = await restorer.RestoreAsync(project, tests, suites, CancellationToken.None);
        Assert.Equal((tests[1].DisplayNumber, UnitTestRestoreItem.Method), (Assert.Single(restored.Items).Number, restored.Items[0].How));
        Assert.Contains("public void Save_WithName_IsAccepted()", harness.ReadFile(TestFile));
        Assert.Contains("public void Save_WithoutName_IsRejected()", harness.ReadFile(TestFile));

        // The file moved: the catalog follows it and nothing is written at the old place.
        var content = harness.ReadFile(TestFile);
        File.Delete(Path.Combine(harness.Project.Root, TestFile));
        harness.Project.Write("tests/Shop.Tests/Users/UserTests.cs", content);
        restored = await restorer.RestoreAsync(project, tests, suites, CancellationToken.None);
        Assert.All(restored.Items, i => Assert.Equal(UnitTestRestoreItem.Moved, i.How));
        Assert.False(File.Exists(Path.Combine(harness.Project.Root, TestFile)));
        Assert.All(await catalog.ListAsync(project.Key, CancellationToken.None), t => Assert.Equal("tests/Shop.Tests/Users/UserTests.cs", t.TestFile));
    }

    [Fact]
    public async Task A_test_without_any_snapshot_is_written_again_from_its_scenario()
    {
        await using var harness = await OrchestrationHarness.CreateAsync();
        var project = await PrepareAsync(harness);
        var catalog = harness.Service<IUnitTestCatalog>();
        var added = await catalog.AddAsync(project.Key,
        [
            new UnitTestCase
            {
                Title = "Diskon member 10%", Framework = TestSuite.XUnit, TestProject = TestProject, TestFile = "tests/Shop.Tests/PriceTests.cs",
                ClassName = "Shop.Tests.PriceTests", MethodName = "Member_GetsTenPercent", Selector = "Shop.Tests.PriceTests.Member_GetsTenPercent",
                Scenario = "given price 100 and a member, when priced, then 90"
            },
            new UnitTestCase
            {
                Title = "Tanpa skenario", Framework = TestSuite.XUnit, TestFile = "tests/Shop.Tests/LostTests.cs", MethodName = "Lost", Selector = "Shop.Tests.LostTests.Lost"
            }
        ], CancellationToken.None);

        var restored = await harness.Service<IUnitTestRestorer>().RestoreAsync(project, added, TestCommandResolver.Suites(project.RootPath, project.Modules), CancellationToken.None);

        var task = Assert.Single(restored.Tasks);
        Assert.Equal(("R1", PlanTask.Create, "tests/Shop.Tests/PriceTests.cs"), (task.Id, task.Action, task.File));
        Assert.Contains("Member_GetsTenPercent", task.Instruction);
        Assert.Contains("given price 100", task.Instruction);
        Assert.Equal(TestFile, Assert.Single(task.Context!).File);
        Assert.Equal([UnitTestRestoreItem.Regenerate, UnitTestRestoreItem.Missing], restored.Items.Select(i => i.How).Order(StringComparer.Ordinal).Reverse());
        Assert.Equal(UnitTestOrigin.Regenerated, (await catalog.GetAsync(added[0].Id, CancellationToken.None))!.Origin);
    }

    [Fact]
    public async Task The_catalog_travels_in_the_memory_export_and_imports_once()
    {
        const string key = "github.com/org/shop";
        MemoryExportFile file;
        await using (var source = await OrchestrationHarness.CreateAsync())
        {
            WriteProject(source.Project);
            await source.Service<IProjectService>().SetKeyAsync(source.ProjectId, key);
            var project = (await source.Service<IProjectService>().GetContextAsync(source.ProjectId))!;
            await source.Service<IUnitTestScanner>().ScanAsync(project, label: false, "andi", CancellationToken.None);
            file = await source.Service<IMemoryTransferService>().ExportAsync(new MemoryQuery(null, null, null, null));
            Assert.Null((await source.Service<IMemoryTransferService>().ExportAsync(new MemoryQuery(null, null, null, null, IncludeUnitTests: false))).UnitTests);
        }

        Assert.Equal(3, file.Version);
        Assert.Equal(2, file.UnitTests!.Count);
        Assert.All(file.UnitTests, t => Assert.Equal((key, Tests), (t.ProjectKey, t.SourceSnapshot!)));

        await using var target = await OrchestrationHarness.CreateAsync();
        var catalog = target.Service<IUnitTestCatalog>();
        // UT-0001 is already taken here by another test of the project.
        await catalog.AddAsync(key, [new UnitTestCase { Title = "Lokal", Framework = TestSuite.XUnit, TestFile = "tests/Local.cs", MethodName = "Local", Selector = "Local.Local" }],
            CancellationToken.None);
        var transfer = target.Service<IMemoryTransferService>();

        var preview = await transfer.ImportAsync(file, MemoryImportMode.Skip, dryRun: true);
        Assert.Equal((2, 0, 2), (preview.UnitTestsAdded, preview.UnitTestsUpdated, preview.UnitTestsRenumbered));
        Assert.Single(await catalog.ListAsync(key, CancellationToken.None));

        var imported = await transfer.ImportAsync(file, MemoryImportMode.Skip, dryRun: false);
        Assert.Equal(2, imported.UnitTestsAdded);
        var tests = await catalog.ListAsync(key, CancellationToken.None);
        Assert.Equal(["UT-0001", "UT-0002", "UT-0003"], tests.Select(t => t.DisplayNumber));
        Assert.Equal(Tests, tests[1].SourceSnapshot);

        var again = await transfer.ImportAsync(file, MemoryImportMode.Skip, dryRun: false);
        Assert.Equal((0, 0, 2), (again.UnitTestsAdded, again.UnitTestsUpdated, again.UnitTestsSkipped));

        // A test removed here never comes back through an older file, even a newer copy of it.
        tests[1].State = UnitTestState.Removed;
        await catalog.UpdateAsync([tests[1]], CancellationToken.None);
        var newer = file with { UnitTests = file.UnitTests.Select(t => t with { UpdatedAt = DateTime.UtcNow.AddHours(1), Title = "Baru" }).ToList() };
        Assert.Equal(2, (await transfer.ImportAsync(newer, MemoryImportMode.Skip, dryRun: false)).UnitTestsUpdated);
        var removed = (await catalog.GetAsync(tests[1].Id, CancellationToken.None))!;
        Assert.Equal((UnitTestState.Removed, "Baru", (string?)null), (removed.State, removed.Title, removed.SourceSnapshot));
    }

    [Fact]
    public async Task Turning_snapshots_off_drops_the_ones_kept()
    {
        await using var harness = await OrchestrationHarness.CreateAsync();
        var project = await PrepareAsync(harness);
        await harness.Service<IUnitTestScanner>().ScanAsync(project, label: false, "dev", CancellationToken.None);
        var catalog = harness.Service<IUnitTestCatalog>();

        await catalog.SaveSettingsAsync(project.Key, new SaveUnitTestSettingsRequest(UnitTestMode.All, UnitTestAuthoring.AutoLogic, false, 120), "dev", CancellationToken.None);

        var settings = await catalog.GetSettingsAsync(project.Key, CancellationToken.None);
        Assert.Equal((UnitTestMode.All, UnitTestAuthoring.AutoLogic, false, (int?)120), (settings.Mode, settings.Authoring, settings.KeepSnapshots, settings.TimeoutSeconds));
        Assert.All(await catalog.ListAsync(project.Key, CancellationToken.None), t => Assert.Null(t.SourceSnapshot));
        await Assert.ThrowsAsync<AppValidationException>(() =>
            catalog.SaveSettingsAsync(project.Key, new SaveUnitTestSettingsRequest(UnitTestMode.All, UnitTestAuthoring.AutoLogic, true, 5), "dev", CancellationToken.None));
    }
}

using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using AIThread.Common.Entities;
using AIThread.Common.Enums;
using AIThread.Service.Interfaces;
using AIThread.Service.Orchestration;
using AIThread.Service.Verification;
using Microsoft.Extensions.DependencyInjection;
using static AIThread.Tests.ThreadOrchestratorTests;
using static AIThread.Tests.UnitTestCatalogTests;

namespace AIThread.Tests;

// Unit tests around a thread's change: the baseline before it, the run after the build, a regression fixed in the code,
// the tester's triage, new tests joining the catalog, obsolete decisions, a thread that only runs tests, and missing
// tests restored before planning. The runner is a fake that answers per test from a script.
public class UnitTestOrchestrationTests
{
    private const string Rejected = "Save_WithoutName_IsRejected";
    private const string Accepted = "Save_WithName_IsAccepted";
    private const string UserLine = "public static class User { public static bool IsValid(string name) => name.Length > 0; }";
    private const string BrokenLine = "public static class User { public static bool IsValid(string name) => true; }";

    [Fact]
    public async Task A_regression_is_fixed_in_the_code_and_the_catalog_learns_the_results()
    {
        var runner = new FakeTestRunner()
            .Then(_ => UnitTestResult.Passed)                                               // baseline
            .Then(t => t.MethodName == Rejected ? UnitTestResult.Failed : UnitTestResult.Passed) // after the change
            .Then(_ => UnitTestResult.Failed)                                               // the retry of the failure
            .Then(_ => UnitTestResult.Passed);                                              // after the fix
        await using var harness = await OrchestrationHarness.CreateAsync(configureServices: s => s.AddSingleton<ITestRunner>(runner));
        await harness.AddStandardNodesAsync();
        var project = await PrepareAsync(harness);
        var router = harness.Router;
        router
            .On(ScriptedAgent.Planner, call =>
            {
                Assert.Contains("Unit tests", call.System);
                Assert.Contains(PlannerTools.RunTests, call.ToolNames!);
                Assert.Contains("# Unit tests", call.LastUser);
                Assert.Contains($"UT-0001 · Save without name is rejected", call.LastUser);
                return router.SubmitPlan(new ThreadPlan
                {
                    Summary = "Validasi nama dihapus.",
                    Tasks = [Step("T1", UserFile, "Accept every name.")],
                    Tests = new PlanTests { Decisions = [new PlanTestDecision { Test = "UT-0002", Decision = "obsolete", Reason = "Fitur nama dihapus." }] }
                });
            })
            .OnExecutor(UserFile, ScriptedRouter.Edit(UserLine, BrokenLine, "Validasi dihapus."))
            .OnExecutor(UserFile, call =>
            {
                Assert.Contains("Unit tests that fail after the change", call.LastUser);
                Assert.Contains("UT-0001", call.LastUser);
                return ScriptedRouter.Edit(BrokenLine, UserLine, "Validasi dikembalikan.");
            })
            .On(ScriptedAgent.Wrapper, ScriptedRouter.Text("Selesai."));

        var (result, observer) = await harness.RunAsync($"Ubah {UserFile}: terima semua nama");

        Assert.Equal(ThreadStatus.Completed, result.Status);
        Assert.Equal(UserLine, harness.ReadFile(UserFile));
        var thread = await harness.LoadThreadAsync(result.ThreadId);
        Assert.Contains(thread.Tasks, t => t.TaskKey == "V1-1" && t.Title == "Fix failing unit tests" && t.FilePath == UserFile);

        var report = UnitTestReport.FromJson(thread.UnitTestJson)!;
        Assert.Equal(UnitTestReport.Passed, report.Outcome);
        Assert.Equal(["UT-0001"], report.Rounds[0].Regressions);
        Assert.Empty(report.Rounds[1].Regressions);
        var first = report.Tests.Single(t => t.Number == "UT-0001");
        Assert.Equal(("passed", "passed", UnitTestReportItem.Ok), (first.Baseline, first.After, first.Verdict));
        Assert.Equal(UnitTestReportItem.Retired, report.Tests.Single(t => t.Number == "UT-0002").Verdict);
        // The retired test does not run after the change.
        Assert.DoesNotContain(runner.Calls.Skip(1), call => call.Contains(Accepted));

        var tests = await harness.Service<IUnitTestCatalog>().ListAsync(project.Key, CancellationToken.None);
        Assert.Equal((UnitTestResult.Passed, UnitTestState.Active), (tests[0].LastResult, tests[0].State));
        Assert.NotNull(tests[0].LastInputHash);
        Assert.Equal((UnitTestState.Obsolete, "Fitur nama dihapus.", result.ThreadId), (tests[1].State, tests[1].ObsoleteReason, tests[1].ObsoleteThreadId!.Value));
        Assert.Contains(await harness.Service<IUnitTestCatalog>().HistoryAsync(tests[0].Id, 10, CancellationToken.None), r => r.Phase == UnitTestRun.Baseline);

        Assert.Contains("unit test lolos", observer.Final);
        Assert.Contains("**🧪 Unit test**", observer.Final);
        Assert.Contains("ditandai obsolete", observer.Final);
        Assert.Contains(observer.Lines, l => l.Contains("Unit test · baseline"));
        Assert.Contains(observer.Lines, l => l.Contains("1 regresi"));
        Assert.Contains("Unit tests after the change", router.CallsTo(ScriptedAgent.Wrapper).Single().LastUser);
    }

    [Fact]
    public async Task The_tester_triages_a_failing_new_test_which_then_joins_the_catalog()
    {
        const string newTest = "Save_WithSpaces_IsRejected";
        var runner = new FakeTestRunner()
            .Then(_ => UnitTestResult.Passed)
            .Then(t => t.MethodName == newTest ? UnitTestResult.Failed : UnitTestResult.Passed)
            .Then(_ => UnitTestResult.Failed)
            .Then(_ => UnitTestResult.Passed);
        await using var harness = await OrchestrationHarness.CreateAsync(configureServices: s => s.AddSingleton<ITestRunner>(runner));
        await harness.AddStandardNodesAsync();
        await harness.AddNodeAsync(NodeRole.Tester, "tester-model");
        var project = await PrepareAsync(harness);
        const string lastTest = "    [Fact]\n    public void Save_WithName_IsAccepted()\n    {\n        Assert.True(User.IsValid(\"Andi\"));\n    }";
        var withNew = lastTest + $"\n\n    [Fact]\n    public void {newTest}()\n    {{\n        Assert.False(User.IsValid(\"  \"));\n    }}";
        var router = harness.Router;
        router
            .On(ScriptedAgent.Planner, router.SubmitPlan(new ThreadPlan
            {
                Summary = "Nama berisi spasi ditolak.",
                Tasks = [Step("T1", UserFile, "Reject blank names."), Step("T2", TestFile, "Add the test.")],
                Tests = new PlanTests
                {
                    New = [new PlanNewTest { Title = "Nama berisi spasi ditolak", Scenario = "given \"  \", when validated, then false", File = TestFile, Name = newTest, Targets = [UserFile] }]
                }
            }))
            .OnExecutor(UserFile, ScriptedRouter.Edit(UserLine, UserLine.Replace("name.Length > 0", "name.Trim().Length > 0"), "Spasi ditolak."))
            .OnExecutor(TestFile, ScriptedRouter.Edit(lastTest, withNew, "Test ditambah."))
            .On(ScriptedAgent.TesterTriage, call =>
            {
                Assert.Contains("## NEW-1 · Nama berisi spasi ditolak", call.LastUser);
                Assert.Contains("Decision: new", call.LastUser);
                return ScriptedRouter.Text($$"""{"summary":"Assert test salah.","verdicts":[{"test":"NEW-1","verdict":"test_wrong","file":"{{TestFile}}","fix":"Use Assert.False on a blank name.","reason":"salah assert"}]}""", 7);
            })
            .OnExecutor(TestFile, call =>
            {
                Assert.Contains("Use Assert.False on a blank name.", call.LastUser);
                return ScriptedRouter.Edit("        Assert.False(User.IsValid(\"  \"));", "        Assert.False(User.IsValid(\"   \"));", "Assert diperbaiki.");
            })
            .On(ScriptedAgent.Wrapper, ScriptedRouter.Text("Selesai."));

        var (result, _) = await harness.RunAsync("Tolak nama yang hanya spasi, buatkan unit test-nya");

        Assert.Equal(ThreadStatus.Completed, result.Status);
        var thread = await harness.LoadThreadAsync(result.ThreadId);
        Assert.Equal(14, thread.TesterTokens);
        Assert.Equal("tester-model", thread.TesterModel);
        Assert.Contains(thread.Tasks, t => t.TaskKey == "V1-1" && t.FilePath == TestFile);
        var report = UnitTestReport.FromJson(thread.UnitTestJson)!;
        var added = report.Tests.Single(t => t.Decision == "new");
        Assert.Equal(("UT-0003", UnitTestReportItem.Ok), (added.Number, added.Verdict));
        Assert.Equal(("Assert test salah.", "tester-model"), (report.Rounds[0].Triage, report.Rounds[0].TesterModel));
        Assert.Equal([TestFile], report.Rounds[0].FixTargets);

        var tests = await harness.Service<IUnitTestCatalog>().ListAsync(project.Key, CancellationToken.None);
        var registered = Assert.Single(tests, t => t.MethodName == newTest);
        Assert.Equal(("UT-0003", UnitTestOrigin.Ait, result.ThreadId, UnitTestResult.Passed), (registered.DisplayNumber, registered.Origin, registered.CreatedThreadId!.Value, registered.LastResult));
        Assert.Equal("given \"  \", when validated, then false", registered.Scenario);
        Assert.Contains(newTest, registered.SourceSnapshot);
    }

    [Fact]
    public async Task A_thread_that_only_runs_tests_restores_missing_ones_first_and_changes_no_code()
    {
        var runner = new FakeTestRunner().Then(t => t.MethodName == Rejected ? UnitTestResult.Failed : UnitTestResult.Passed).Then(_ => UnitTestResult.Failed);
        await using var harness = await OrchestrationHarness.CreateAsync(configureServices: s => s.AddSingleton<ITestRunner>(runner));
        await harness.AddStandardNodesAsync();
        var project = await PrepareAsync(harness);
        await harness.Service<IUnitTestScanner>().ScanAsync(project, label: false, "andi", CancellationToken.None);
        File.Delete(Path.Combine(harness.Project.Root, TestFile));
        var router = harness.Router;
        router
            .On(ScriptedAgent.Planner, call =>
            {
                // Restored before the planner looks.
                Assert.True(File.Exists(Path.Combine(harness.Project.Root, TestFile)));
                return router.SubmitPlan(new ThreadPlan { Summary = "Menjalankan semua unit test.", Answer = "Semua unit test dijalankan.", Tests = new PlanTests { Run = ["all"] } });
            })
            .On(ScriptedAgent.Wrapper, call =>
            {
                Assert.Contains("No file was changed", call.LastUser);
                return ScriptedRouter.Text("Satu test gagal.");
            });

        var (result, observer) = await harness.RunAsync("Jalankan semua unit test");

        Assert.Equal(ThreadStatus.Completed, result.Status);
        Assert.Equal(UnitTestCatalogTests.Tests.ReplaceLineEndings("\n"), harness.ReadFile(TestFile));
        var thread = await harness.LoadThreadAsync(result.ThreadId);
        Assert.Empty(thread.Tasks);
        Assert.Equal("Unit test: 1 gagal (UT-0001).", thread.Error);
        var report = UnitTestReport.FromJson(thread.UnitTestJson)!;
        Assert.True(report.TestOnly);
        Assert.Equal(UnitTestReport.Failed, report.Outcome);
        Assert.Equal(2, report.Restored.Count);
        Assert.Equal(UnitTestReportItem.Failing, report.Tests.Single(t => t.Number == "UT-0001").Verdict);
        Assert.Equal(UnitTestResult.Failed, (await harness.Service<IUnitTestCatalog>().ListAsync(project.Key, CancellationToken.None))[0].LastResult);

        Assert.Contains(observer.Lines, l => l.Contains("Pemulihan unit test"));
        Assert.Contains("Pengujian selesai, ada yang gagal", observer.Final);
        Assert.Contains("**UT-0001**", observer.Final);
        Assert.Empty(router.CallsTo(ScriptedAgent.Executor));
    }

    [Fact]
    public async Task Tests_the_planner_ran_are_not_run_again_and_the_brief_says_what_the_catalog_covers()
    {
        var runner = new FakeTestRunner();
        await using var harness = await OrchestrationHarness.CreateAsync(configureServices: s => s.AddSingleton<ITestRunner>(runner));
        await harness.AddStandardNodesAsync();
        await PrepareAsync(harness);
        var router = harness.Router;
        router
            .On(ScriptedAgent.Planner, call =>
            {
                // No file named: no related test, and the brief tells which tests the catalog has instead.
                Assert.Contains($"The catalog covers only: {TestFile} (2).", call.LastUser);
                return router.Tool(PlannerTools.RunTests, new { all = true });
            })
            .On(ScriptedAgent.Planner, call =>
            {
                Assert.Contains("2 passed", call.Messages.ToJsonString());
                return router.SubmitPlan(new ThreadPlan { Summary = "Menjalankan semua unit test.", Tests = new PlanTests { Run = ["all"] } });
            })
            .On(ScriptedAgent.Wrapper, ScriptedRouter.Text("Semua lolos."));

        var (result, observer) = await harness.RunAsync("Jalankan semua unit test");

        Assert.True(result.Status == ThreadStatus.Completed, (await harness.LoadThreadAsync(result.ThreadId)).Error);
        Assert.Single(runner.Calls);
        Assert.Contains(observer.Lines, l => l.Contains("2 dari run_tests planner"));
        var thread = await harness.LoadThreadAsync(result.ThreadId);
        Assert.Equal(UnitTestReport.Passed, UnitTestReport.FromJson(thread.UnitTestJson)!.Outcome);
        // One run per test in the history, recorded as a manual run.
        var catalog = harness.Service<IUnitTestCatalog>();
        foreach (var test in await catalog.ListAsync((await harness.Service<IProjectService>().GetContextAsync(harness.ProjectId))!.Key, CancellationToken.None))
            Assert.Equal([UnitTestRun.Manual], (await catalog.HistoryAsync(test.Id, 10, CancellationToken.None)).Select(r => r.Phase));
    }

    [Fact]
    public async Task Without_a_runnable_suite_a_project_runs_no_unit_tests()
    {
        var runner = new FakeTestRunner();
        await using var harness = await CreateAsync();
        await harness.Service<IProjectService>().SetKeyAsync(harness.ProjectId, "github.com/org/greeter");
        var router = harness.Router;
        router
            .On(ScriptedAgent.Planner, call =>
            {
                Assert.DoesNotContain("# Unit tests", call.LastUser);
                Assert.DoesNotContain(PlannerTools.RunTests, call.ToolNames!);
                return router.SubmitPlan(new ThreadPlan { Summary = "Sapaan.", Tasks = [Step("T1", GreeterFile, "Return \"Halo\".")] });
            })
            .OnExecutor(GreeterFile, ScriptedRouter.Edit(HelloLine, HalloLine, "Sapaan diganti."))
            .On(ScriptedAgent.Wrapper, ScriptedRouter.Text("Selesai."));

        var (result, _) = await harness.RunAsync("Sapaan jadi Halo");

        Assert.Equal(ThreadStatus.Completed, result.Status);
        Assert.Null((await harness.LoadThreadAsync(result.ThreadId)).UnitTestJson);
        Assert.Empty(runner.Calls);
    }

    // Answers each test of a run from the next script step (the last step repeats); records the methods per run.
    private sealed class FakeTestRunner : ITestRunner
    {
        private readonly List<Func<TestTarget, UnitTestResult>> _steps = [];
        private readonly ConcurrentQueue<IReadOnlyList<string>> _calls = new();
        private int _next;

        public IReadOnlyList<IReadOnlyList<string>> Calls => _calls.ToList();

        public FakeTestRunner Then(Func<TestTarget, UnitTestResult> step)
        {
            _steps.Add(step);
            return this;
        }

        public Task<TestRunResult> RunAsync(TestSuite suite, IReadOnlyList<TestTarget>? targets, TimeSpan timeout, CancellationToken cancellationToken)
        {
            var wanted = targets ?? [];
            _calls.Enqueue(wanted.Select(t => t.MethodName).ToList());
            var step = _steps.Count == 0 ? _ => UnitTestResult.Passed : _steps[Math.Min(Interlocked.Increment(ref _next) - 1, _steps.Count - 1)];
            var outcomes = wanted.Select(t =>
            {
                var result = step(t);
                return new TestCaseOutcome(t.ClassName, t.MethodName, null, result, 4, result == UnitTestResult.Failed ? $"Assert failed in {t.MethodName}" : null);
            }).ToList();
            var command = TestCommandResolver.Command(suite, targets, Path.Combine(Path.GetTempPath(), "fake-report.json"));
            return Task.FromResult(new TestRunResult(command, false, outcomes.Any(o => o.Result == UnitTestResult.Failed) ? 1 : 0, outcomes, string.Empty, 10));
        }
    }
}

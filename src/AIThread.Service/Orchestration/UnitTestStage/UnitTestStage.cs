using System.Collections.Concurrent;
using System.Text.Json;
using AIThread.Common.Entities;
using AIThread.Common.Enums;
using AIThread.Service.Interfaces;
using AIThread.Service.Models;
using AIThread.Service.UnitTests;
using AIThread.Service.Verification;
using AIThread.Service.Workspace;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.Orchestration;

// One thread's work with the project's unit test catalog: the settings and suites, the catalog as it was when the thread
// started, the results before the change (baseline) and after it, and the report the thread page shows.
public sealed class UnitTestSession
{
    public required ProjectContext Project { get; init; }
    public required UnitTestSettings Settings { get; init; }
    public required IReadOnlyList<TestSuite> Suites { get; init; }
    public required List<UnitTestCase> Tests { get; init; }
    public required TimeSpan Timeout { get; init; }
    public required string Developer { get; init; }
    public string Machine { get; } = Environment.MachineName;

    // The last run of each test on this machine: a test skipped here for its environment is not run again soon.
    public IReadOnlyDictionary<Guid, UnitTestRun> MachineRuns { get; init; } = new Dictionary<Guid, UnitTestRun>();

    public UnitTestReport Report { get; } = new();

    // Results before the change by test id, from a run now or (Cached) from the catalog for the same code.
    public ConcurrentDictionary<Guid, TestVerdict> Baseline { get; } = new();
    public ConcurrentDictionary<Guid, bool> Cached { get; } = new();
    public Task? BaselineRun { get; set; }

    // The tests the change concerns: run before and after it.
    public HashSet<Guid> Relevant { get; } = [];

    // Results after the change by test id (new tests included), the latest round winning.
    public Dictionary<Guid, TestVerdict> After { get; } = [];

    // Tests the plan adds, as found in their files after the executors ran; registered when the thread ends.
    public List<UnitTestCase> NewTests { get; } = [];

    // Tests the tester found unstable: quarantined when the thread ends, with the reason.
    public Dictionary<Guid, string> Quarantine { get; } = [];

    public Dictionary<Guid, UnitTestReportItem> Items { get; } = [];

    public WorkspaceScope Scope => new(Project.RootPath);

    public IEnumerable<UnitTestCase> Active => Tests.Where(t => t.State == UnitTestState.Active);

    public UnitTestRules Rules => new(Active.GroupBy(t => t.DisplayNumber).ToDictionary(g => g.Key, g => g.First().TestFile, StringComparer.OrdinalIgnoreCase),
        Settings.Authoring != UnitTestAuthoring.Off);

    public bool CanRun => Suites.Any(s => s.CanRun);

    public UnitTestCase? ByNumber(string number) => Active.FirstOrDefault(t => string.Equals(t.DisplayNumber, number, StringComparison.OrdinalIgnoreCase));
}

// Stored on the thread as JSON (ThreadRun.UnitTestJson) for the thread page.
public sealed class UnitTestReport
{
    public const string Passed = "passed";
    public const string Failed = "failed";
    public const string Skipped = "skipped";

    public string Outcome { get; set; } = Skipped;
    public string Mode { get; set; } = string.Empty;
    public List<string> Suites { get; set; } = [];
    public List<string> Problems { get; set; } = [];
    public List<UnitTestRestoreItem> Restored { get; set; } = [];
    public List<string> Notes { get; set; } = [];
    public int BaselineRan { get; set; }
    public int BaselineCached { get; set; }
    public long BaselineMs { get; set; }
    public List<UnitTestRoundReport> Rounds { get; set; } = [];
    public List<UnitTestReportItem> Tests { get; set; } = [];
    public string? TesterModel { get; set; }
    public string? Summary { get; set; }

    // True when only tests were asked for and no code changed.
    public bool TestOnly { get; set; }

    public int Count(string verdict) => Tests.Count(t => t.Verdict == verdict);

    public string ToJson() => JsonSerializer.Serialize(this, ThreadPlan.JsonOptions);

    public static UnitTestReport? FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<UnitTestReport>(json, ThreadPlan.JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

// One test in the thread's report. Results are lower-case UnitTestResult names; Baseline null = not run before.
public sealed class UnitTestReportItem
{
    public const string Ok = "ok";
    public const string Fixed = "fixed";
    public const string Regression = "regression";
    public const string Preexisting = "preexisting";
    public const string NewFailing = "new-failing";
    public const string Failing = "failing";
    public const string SkippedVerdict = "skipped";
    public const string NotFound = "notfound";
    public const string Missing = "missing";
    public const string Flaky = "flaky";

    // Retired by the plan: waits for the developer to confirm its removal.
    public const string Retired = "obsolete";

    public Guid? Id { get; set; }
    public string Number { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? File { get; set; }
    public string? Decision { get; set; }
    public string? Baseline { get; set; }
    public bool BaselineCached { get; set; }
    public string? After { get; set; }
    public string Verdict { get; set; } = string.Empty;
    public string? Message { get; set; }
    public string? SkipReason { get; set; }
    public string? Triage { get; set; }
    public string? TriageReason { get; set; }
    public long? DurationMs { get; set; }

    // Still a problem when the thread ends: the change broke it, or a test it added fails.
    public bool IsProblem => Verdict is Regression or NewFailing;
}

// One run of the unit tests in a verification round.
public sealed class UnitTestRoundReport
{
    public int Round { get; set; }
    public int Ran { get; set; }
    public int Passed { get; set; }
    public int Failed { get; set; }
    public int Skipped { get; set; }
    public long DurationMs { get; set; }
    public List<string> Commands { get; set; } = [];
    public List<string> Regressions { get; set; } = [];
    public List<string> Preexisting { get; set; } = [];
    public string? Triage { get; set; }
    public string? TesterModel { get; set; }
    public List<string> FixTargets { get; set; } = [];
}

// ChangedFiles: every file the thread changed so far (fix tasks included). Tester: the node that triages failures,
// null for the mechanical rule (fix the code under test).
public sealed record UnitTestCheckInput(
    ThreadRequest Request,
    ThreadPlan Plan,
    IReadOnlyList<PlanTask> Tasks,
    IReadOnlyList<TaskResult> Results,
    IReadOnlyCollection<string> ChangedFiles,
    int Round,
    AiNode? Tester,
    Guid ThreadId);

// Issues: what fix tasks must change (the code, or a test of this change). Problems: tests that still fail as a
// regression or a new test, fixable or not.
public sealed record UnitTestCheck(UnitTestRoundReport Round, IReadOnlyList<ReviewIssue> Issues, IReadOnlyList<string> Unfixable, int Problems, int TesterTokens, string ForModels);

// Runs the project's unit tests around a thread's change: missing tests are restored first, the related tests run before
// the change (in parallel with the planner) and again after every build that passes, a test that passed before and
// fails after is a regression the verification has fixed (in the code), and the results go to the catalog at the end.
public sealed partial class UnitTestStage(
    IUnitTestCatalog catalog,
    IUnitTestExecutor executor,
    IUnitTestRestorer restorer,
    IUnitTestScanner scanner,
    ITesterAgent testerAgent,
    ICodeMapper codeMapper,
    ILogger<UnitTestStage> logger) : IUnitTestStage
{
    // Regression fix rounds of one thread (the developer's choice), within the verification's own fix rounds.
    public const int MaxFixRounds = 3;

    // An earlier result of the same code stands in for the baseline for this long.
    private static readonly TimeSpan CacheAge = TimeSpan.FromDays(14);

    // How long a thread waits for the catalog's store before it goes on without unit tests.
    private static readonly TimeSpan CatalogTimeout = TimeSpan.FromSeconds(10);

    // A test skipped on this machine for its environment is skipped again without running for this long.
    private static readonly TimeSpan EnvironmentSkipAge = TimeSpan.FromHours(24);

    private const int MaxBriefTests = 30;
    private const int MaxBriefTestFiles = 10;
    private const int MaxRestoreLines = 8;
    private const int MaxOneLineChars = 140;
    private const int MaxProgressFailures = 5;
    private const int MaxMessageChars = 600;
    private const int MaxSourceChars = 2_500;
}

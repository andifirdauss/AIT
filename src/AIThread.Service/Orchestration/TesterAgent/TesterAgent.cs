using System.Text.Json;
using AIThread.Common.Entities;
using AIThread.Service.Interfaces;
using AIThread.Service.Models;
using AIThread.Service.Testing;

namespace AIThread.Service.Orchestration;

// Plan and Execution include the verification fix tasks. Login: given by the developer for this test (TestLogin);
// CanAskLogin: the developer can answer a login question in the chat, so the tester gets ask_login.
public sealed record TestingInput(
    AiNode Tester,
    ThreadRequest Request,
    ThreadPlan Plan,
    ExecutionSummary Execution,
    ProjectContext Project,
    TestLogin? Login = null,
    bool CanAskLogin = false);

// ForModels describes the test in English for the wrapper. Node is the tester that answered (a fallback when the given
// one was unavailable).
public sealed record TestingResult(TestingReport Report, int Tokens, string ForModels, AiNode Node);

// A unit test that fails after the change, as the tester triages it. Test: its number, or NEW-<n> for a test of this
// change; Decision: keep, update or new.
public sealed record UnitTestFailure(string Test, string Title, string? Purpose, string Decision, string TestFile, string? Source, string? Message);

public sealed record UnitTestTriageInput(
    AiNode Tester,
    ThreadRequest Request,
    ThreadPlan Plan,
    IReadOnlyList<PlanTask> Tasks,
    IReadOnlyList<TaskResult> Results,
    IReadOnlyList<UnitTestFailure> Failures);

// Verdict: regression | test_wrong | plan_wrong | environment | flaky.
public sealed record UnitTestTriageVerdict(string Test, string Verdict, string? File, string? Anchor, string? Fix, string? Reason)
{
    public const string Regression = "regression";
    public const string TestWrong = "test_wrong";
    public const string PlanWrong = "plan_wrong";
    public const string Environment = "environment";
    public const string Flaky = "flaky";
}

// Error: the tester could not be asked or answered unreadably; the failures are then handled by the mechanical rule.
public sealed record UnitTestTriageResult(IReadOnlyList<UnitTestTriageVerdict> Verdicts, string? Summary, int Tokens, AiNode Node, string? Error = null);

// Stored on the thread as JSON (ThreadRun.TestingJson) for the thread page.
public sealed class TestingReport
{
    public const string Passed = "passed";
    public const string Failed = "failed";
    public const string Blocked = "blocked";

    // The plan's level is none, or the change cannot be loaded (the build still fails): nothing was run.
    public const string Skipped = "skipped";

    public string Outcome { get; set; } = Blocked;
    public string Level { get; set; } = TestPlan.None;
    public string? BaseUrl { get; set; }
    public string? TesterModel { get; set; }
    public string? Browser { get; set; }
    public string? Summary { get; set; }

    // Why the test could not run at all: the app is not reachable, the browser does not start, ...
    public string? Error { get; set; }

    public int Steps { get; set; }
    public List<TestScenarioResult> Scenarios { get; set; } = [];
    public List<HttpExchange> Requests { get; set; } = [];

    // Console errors and failed requests the browser saw during the run.
    public List<string> BrowserErrors { get; set; } = [];

    // The command AIT started the app with because it was not running (stopped again after the test).
    public string? AppStarted { get; set; }

    // The command AIT tried when it could not start the app (it appears in Error), and the last lines the app printed
    // (build warnings left out), for the developer.
    public string? AppCommand { get; set; }
    public string? AppOutput { get; set; }

    // The username the test ran with (never the password).
    public string? LoginUser { get; set; }

    // Set when the tester stopped to ask for a login (ask_login): what needed it, and the login page.
    public string? LoginNeeded { get; set; }
    public string? LoginUrl { get; set; }

    // The question went to the chat: the session's next "login <user> <password>" runs the test again.
    public bool AskedLogin { get; set; }

    public int Count(string status) => Scenarios.Count(s => s.Status == status);

    public string ToJson() => JsonSerializer.Serialize(this, ThreadPlan.JsonOptions);

    public static TestingReport? FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<TestingReport>(json, ThreadPlan.JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // One line for the thread record (read_thread, the context keeper): the outcome and every scenario's verdict.
    public string Brief()
    {
        var parts = new List<string> { $"{Outcome} ({Level}{(BaseUrl is null ? string.Empty : $" at {BaseUrl}")})" };
        if (Error is not null) parts.Add(Error);
        parts.AddRange(Scenarios.Select(s => $"[{s.Status}] {s.Title}" + (string.IsNullOrWhiteSpace(s.Details) || s.Details == Error ? string.Empty : $": {s.Details}")));
        if (Outcome == Skipped && Summary is not null) parts.Add(Summary);
        if (LoginNeeded is not null) parts.Add($"needs a login: {LoginNeeded}");
        return string.Join("; ", parts);
    }
}

public sealed class TestScenarioResult
{
    public string Title { get; set; } = string.Empty;

    // passed | failed | blocked
    public string Status { get; set; } = TestingReport.Blocked;
    public string? Details { get; set; }
}

// One http_request of the tester, as the developer sees it.
public sealed class HttpExchange
{
    public string Method { get; set; } = "GET";
    public string Url { get; set; } = string.Empty;
    public string? RequestBody { get; set; }
    public int? Status { get; set; }
    public string? Reason { get; set; }
    public string? ContentType { get; set; }

    // Redirect target; redirects are not followed.
    public string? Location { get; set; }
    public string? ResponseBody { get; set; }
    public long DurationMs { get; set; }

    // No response at all (connection refused, timeout).
    public string? Error { get; set; }
}

// The tester node checks a verified change on the running app in its own tool-calling loop: pages in a visible browser
// (ITestBrowserLauncher), endpoints with HTTP requests, both only on localhost (TestUrls). An app that is not running is
// started for the test and stopped after it (IAppLauncher). It never changes code and never fails the thread: a test that
// cannot run is reported as blocked.
public sealed partial class TesterAgent(INodeCaller caller, INodePromptBuilder prompts, ITestBrowserLauncher browsers, IAppLauncher apps, ISecretGuard secrets) : ITesterAgent
{
    private const int MaxSteps = 40;
    private const int MaxDiffLinesPerTask = 80;
    private const int MaxInstructionChars = 600;

    // Triage of unit tests: more of each diff, since the failing code may sit anywhere in it.
    private const int MaxTriageDiffLines = 150;

    // A response body as the model reads it, and as the report keeps it.
    private const int MaxResponseChars = 6_000;
    private const int MaxStoredBodyChars = 4_000;
    private const int MaxReadBytes = 512 * 1024;
    private const int MaxStoredRequests = 30;

    // Lines of a response body shown under its progress line.
    private const int MaxProgressBodyLines = 8;
    private const int MaxProgressLineChars = 100;

    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(5);

    // The finished page stays on screen a moment before the window closes.
    private static readonly TimeSpan Linger = TimeSpan.FromSeconds(2);

    // One test run: its own cookies (a login carries over to the next request), and the browser once a browser tool
    // asked for it.
    private sealed class TestRun(TestingInput input, TestingReport report, HttpClient http, ISecretGuard secrets)
    {
        public ISecretGuard Secrets { get; } = secrets;
        public TestingInput Input { get; } = input;
        public TestLogin? Login => Input.Login;
        public TestingReport Report { get; } = report;
        public HttpClient Http { get; } = http;
        public TestPlan Test => Input.Plan.Test!;
        public ITestBrowser? Browser { get; set; }

        // The app this run started because it was not running; stopped when the run ends.
        public RunningApp? App { get; set; }

        // Set when the browser could not start; later browser tools answer with it instead of trying again.
        public string? BrowserError { get; set; }

        // The password back to its placeholder, in anything the model, the log or the report get, and the secrets the
        // app answered with (a token from a login endpoint) masked too; RunToolAsync writes them back into later calls.
        public string Mask(string text) => Secrets.Redact(Login?.Mask(text) ?? text);
    }
}

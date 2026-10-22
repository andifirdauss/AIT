using System.Text.Json;
using AIThread.Common.Entities;
using AIThread.Service.Interfaces;
using AIThread.Service.Models;
using AIThread.Service.Verification;
using AIThread.Service.Workspace;

namespace AIThread.Service.Orchestration;

public sealed record VerificationInput(
    ThreadRequest Request,
    ThreadPlan Plan,
    ExecutionSummary Execution,
    BuildCommand? Build,
    AiNode? Reviewer,
    IReadOnlyList<AiNode> Executors,
    WorkspaceScope Scope,
    RuntimeSettings Settings,
    int NextTaskOrder,
    FileJournal? Journal = null,
    // Number of the first round; a retried thread continues after its earlier rounds so fix task ids stay unique.
    int FirstRound = 1,
    // The project's unit tests (null: none run), and the tester node that triages their failures.
    UnitTestSession? UnitTests = null,
    AiNode? Tester = null);

// Plan and Execution include the fix tasks, ready for the wrapper and memory extraction. ForModels describes the
// rounds in English for the wrapper. TesterTokens: spent triaging unit test failures.
public sealed record VerificationResult(
    VerificationReport Report,
    ThreadPlan Plan,
    ExecutionSummary Execution,
    int ExecutorTokens,
    int ReviewerTokens,
    string ForModels,
    int TesterTokens = 0);

// Stored on the thread as JSON (ThreadRun.VerificationJson) for the thread page.
public sealed class VerificationReport
{
    public const string Passed = "passed";
    public const string Failed = "failed";
    public const string Unverified = "unverified";

    public string Outcome { get; set; } = Unverified;
    public string? Command { get; set; }
    public string? ReviewerModel { get; set; }
    public List<VerificationRound> Rounds { get; set; } = [];

    public string ToJson() => JsonSerializer.Serialize(this, ThreadPlan.JsonOptions);
}

public sealed class VerificationRound
{
    public int Round { get; set; }
    public BuildRoundReport? Build { get; set; }
    public ReviewRoundReport? Review { get; set; }

    // The project's unit tests after this round's build (null when they did not run).
    public UnitTestRoundReport? UnitTests { get; set; }

    public List<string> FixTasks { get; set; } = [];
    public List<string> Unfixable { get; set; } = [];
}

public sealed class BuildRoundReport
{
    // passed | failed | locked (compiled, output files locked) | timeout | error (could not start)
    public string Status { get; set; } = string.Empty;
    public string Headline { get; set; } = string.Empty;
    public int ErrorCount { get; set; }
    public int WarningCount { get; set; }
    public long DurationMs { get; set; }
    public List<string> Errors { get; set; } = [];
    public string? Output { get; set; }
}

public sealed class ReviewRoundReport
{
    public bool Approved { get; set; }
    public string? Summary { get; set; }
    public List<ReviewIssue> Issues { get; set; } = [];
    public string? Error { get; set; }
}

// After the executors: build the project and/or let the reviewer node check the changes, run the project's unit tests
// on a build that passes, then hand the problems to executors as fix tasks and check again, for at most MaxFixRounds
// rounds (unit test regressions: UnitTestStage.MaxFixRounds). Never fails the thread: problems that remain are reported
// to the developer.
public sealed partial class VerificationStage(IBuildRunner buildRunner, IReviewerAgent reviewerAgent, IExecutionEngine executionEngine, IUnitTestStage unitTestStage)
    : IVerificationStage
{
    private const int MaxErrorsInReport = 30;
    private const int MaxErrorsInProgress = 5;
    private const int MaxOutputChars = 3000;
}

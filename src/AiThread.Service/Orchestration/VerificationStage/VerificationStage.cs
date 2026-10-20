using System.Text.Json;
using AiThread.Common.Entities;
using AiThread.Service.Interfaces;
using AiThread.Service.Models;
using AiThread.Service.Verification;
using AiThread.Service.Workspace;

namespace AiThread.Service.Orchestration;

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
    int FirstRound = 1);

// Plan and Execution include the fix tasks, ready for the wrapper and memory extraction. ForModels describes the
// rounds in English for the wrapper.
public sealed record VerificationResult(
    VerificationReport Report,
    ThreadPlan Plan,
    ExecutionSummary Execution,
    int ExecutorTokens,
    int ReviewerTokens,
    string ForModels);

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

// After the executors: build the project and/or let the reviewer node check the changes, then hand the problems to
// executors as fix tasks and check again, for at most MaxFixRounds rounds. Never fails the thread: problems that
// remain are reported to the developer.
public sealed partial class VerificationStage(IBuildRunner buildRunner, IReviewerAgent reviewerAgent, IExecutionEngine executionEngine) : IVerificationStage
{
    private const int MaxErrorsInReport = 30;
    private const int MaxErrorsInProgress = 5;
    private const int MaxOutputChars = 3000;
}

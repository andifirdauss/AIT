using AIThread.Common.Enums;

namespace AIThread.Common.Entities;

// One orchestrated request: planner -> parallel executors -> wrapper.
public class ThreadRun : BaseEntity
{
    public Guid? ProjectId { get; set; }
    public Project? Project { get; set; }

    public ThreadSource Source { get; set; }
    public string? SessionId { get; set; }
    public string? WorkingDirectory { get; set; }
    public string Prompt { get; set; } = string.Empty;
    public ThreadStatus Status { get; set; }

    public string? PlanSummary { get; set; }
    public string? PlanJson { get; set; }
    public string? FinalResponse { get; set; }
    public string? Error { get; set; }

    public string? PlannerModel { get; set; }
    public string? WrapperModel { get; set; }
    public string? ReviewerModel { get; set; }
    public string? TesterModel { get; set; }
    public int PlannerSteps { get; set; }
    public int PlannerTokens { get; set; }

    // Part of PlannerTokens served from the provider's prompt cache.
    public int PlannerCachedTokens { get; set; }

    // Spent by tools nodes on the investigations the planner delegated to them.
    public int ToolsNodeTokens { get; set; }

    // Includes the fix tasks of the verification stage.
    public int ExecutorTokens { get; set; }
    public int ReviewerTokens { get; set; }
    public int TesterTokens { get; set; }
    public int WrapperTokens { get; set; }

    public long? PlanningMs { get; set; }

    // Wall-clock time of the execution stage (bounded by the slowest executor).
    public long? ExecutionWallMs { get; set; }

    // Sum of every executor duration — what a sequential pipeline would have cost.
    public long? ExecutionSumMs { get; set; }

    // Build + review + fix rounds after execution; null when the stage did not run.
    public long? VerificationMs { get; set; }

    // VerificationReport as JSON: one entry per round with the build result and the reviewer's verdict.
    public string? VerificationJson { get; set; }

    // Auto Testing after verification; null when it did not run.
    public long? TestingMs { get; set; }

    // TestingReport as JSON: the scenarios with their verdict and the HTTP requests the tester sent.
    public string? TestingJson { get; set; }

    // Unit tests of the project catalog (baseline, the runs after the change, the tester's triage); null when none ran.
    public long? UnitTestMs { get; set; }

    // UnitTestReport as JSON: every test with its result before and after the change, and the tests restored.
    public string? UnitTestJson { get; set; }

    public long? WrappingMs { get; set; }
    public long? TotalMs { get; set; }
    public DateTime? CompletedAt { get; set; }

    // Summary of the conversation up to and including this thread, written by the context keeper. The next planner
    // run of the same chat starts from it instead of the full history.
    public string? ContextPack { get; set; }

    // InvestigationLog as JSON: the files, lookups and sub agent findings the planner went through. A follow-up in the
    // same chat starts from it instead of searching again.
    public string? InvestigationJson { get; set; }

    // Times the unfinished tasks of this thread were run again from its stored plan.
    public int RetryCount { get; set; }

    public DateTime? RolledBackAt { get; set; }

    // When the business wiki writer summarized this thread into business memory; null = not yet.
    public DateTime? BusinessDigestedAt { get; set; }

    public ICollection<ThreadTask> Tasks { get; set; } = [];
}

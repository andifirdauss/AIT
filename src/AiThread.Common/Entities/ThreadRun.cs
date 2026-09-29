using AiThread.Common.Enums;

namespace AiThread.Common.Entities;

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
    public int PlannerSteps { get; set; }
    public int PlannerTokens { get; set; }
    public int ExecutorTokens { get; set; }
    public int WrapperTokens { get; set; }

    public long? PlanningMs { get; set; }

    // Wall-clock time of the execution stage (bounded by the slowest executor).
    public long? ExecutionWallMs { get; set; }

    // Sum of every executor duration — what a sequential pipeline would have cost.
    public long? ExecutionSumMs { get; set; }

    public long? WrappingMs { get; set; }
    public long? TotalMs { get; set; }
    public DateTime? CompletedAt { get; set; }

    // Summary of the conversation up to and including this thread, written by the context keeper. The next planner
    // run of the same chat starts from it instead of the full history.
    public string? ContextPack { get; set; }

    public ICollection<ThreadTask> Tasks { get; set; } = [];
}

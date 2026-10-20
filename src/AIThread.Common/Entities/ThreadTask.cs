using AIThread.Common.Enums;

namespace AIThread.Common.Entities;

// One atomic unit of work handed to an executor: a single file, a single instruction.
public class ThreadTask : BaseEntity
{
    public Guid ThreadRunId { get; set; }
    public ThreadRun? ThreadRun { get; set; }

    public string TaskKey { get; set; } = string.Empty;
    public int Order { get; set; }
    public string Title { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string Action { get; set; } = "edit";

    // Destination of a move task, relative to the project root; null for other actions.
    public string? TargetPath { get; set; }

    public string Instruction { get; set; } = string.Empty;

    // Comma-separated task keys this task waits for.
    public string? DependsOn { get; set; }

    public ThreadTaskStatus Status { get; set; }
    public Guid? NodeId { get; set; }
    public string? NodeName { get; set; }
    public string? Model { get; set; }
    public int Attempts { get; set; }
    public string? Summary { get; set; }
    public string? Diff { get; set; }
    public string? Error { get; set; }
    public int Tokens { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public long? DurationMs { get; set; }
}

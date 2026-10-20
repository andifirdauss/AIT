using AIThread.Common.Entities;

namespace AIThread.Service.Orchestration;

public sealed partial class ThreadOrchestrator
{
    private static TaskResult ToResult(ThreadTask row) =>
        new(row.TaskKey, row.FilePath, row.Status, row.Summary, row.Diff, row.Error, row.DurationMs ?? 0, row.Tokens);
}

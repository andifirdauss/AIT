using AIThread.Common.Entities;
using AIThread.Common.Enums;

namespace AIThread.Service.Orchestration;

public sealed partial class ExecutionEngine
{
    private static async Task<TaskResult> SkipAsync(PlanTask task, ThreadTask entity, string reason, TaskLine columns, ThreadRecorder recorder, ClientProgress progress, CancellationToken cancellationToken)
    {
        await recorder.UpdateTaskAsync(entity, e =>
        {
            e.Status = ThreadTaskStatus.Skipped;
            e.Error = reason;
            e.CompletedAt = DateTime.UtcNow;
        });
        await columns.WriteAsync(progress, "⏩", task.Id, "—", "—", $"dilewati — {reason}", cancellationToken);
        return new TaskResult(task.Id, task.File, ThreadTaskStatus.Skipped, null, null, reason, 0, 0);
    }
}

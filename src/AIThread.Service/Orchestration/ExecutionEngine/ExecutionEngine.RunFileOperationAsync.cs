using System.Diagnostics;
using AIThread.Common.Entities;
using AIThread.Common.Enums;

namespace AIThread.Service.Orchestration;

public sealed partial class ExecutionEngine
{
    // No executor node and no global slot: a delete or move takes milliseconds.
    private async Task<TaskResult> RunFileOperationAsync(
        PlanTask task,
        ThreadTask entity,
        ExecutionRequest request,
        TaskLine columns,
        ThreadRecorder recorder,
        ClientProgress progress,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        await recorder.UpdateTaskAsync(entity, e =>
        {
            e.Status = ThreadTaskStatus.Running;
            e.StartedAt = DateTime.UtcNow;
        });

        ExecutorOutcome outcome;
        try
        {
            outcome = await fileOperations.RunAsync(task, request.Scope, request.Journal, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            outcome = new ExecutorOutcome(false, null, null, ex.Message, 0, 1);
        }

        stopwatch.Stop();
        return await FinishAsync(task, entity, outcome, stopwatch, NoModel, columns, recorder, progress, cancellationToken);
    }
}

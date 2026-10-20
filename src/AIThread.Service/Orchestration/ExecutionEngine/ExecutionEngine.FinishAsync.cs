using System.Diagnostics;
using AIThread.Common.Entities;
using AIThread.Common.Enums;

namespace AIThread.Service.Orchestration;

public sealed partial class ExecutionEngine
{
    private static async Task<TaskResult> FinishAsync(
        PlanTask task,
        ThreadTask entity,
        ExecutorOutcome outcome,
        Stopwatch stopwatch,
        string? doneBy,
        TaskLine columns,
        ThreadRecorder recorder,
        ClientProgress progress,
        CancellationToken cancellationToken)
    {
        var status = outcome.Success ? ThreadTaskStatus.Succeeded : ThreadTaskStatus.Failed;
        await recorder.UpdateTaskAsync(entity, e =>
        {
            e.Status = status;
            e.Summary = outcome.Summary;
            e.Diff = outcome.Diff;
            e.Error = outcome.Error;
            e.Tokens = outcome.Tokens;
            e.Attempts = outcome.Attempts;
            e.CompletedAt = DateTime.UtcNow;
            e.DurationMs = stopwatch.ElapsedMilliseconds;
        });

        var seconds = $"{stopwatch.Elapsed.TotalSeconds:0.0}s";
        await columns.WriteAsync(progress, outcome.Success ? "✅" : "❌", task.Id, seconds, doneBy ?? NoModel,
            (outcome.Success ? outcome.Summary : outcome.Error) ?? string.Empty, cancellationToken);
        recorder.Log(outcome.Success ? ActivityLevel.Success : ActivityLevel.Error,
            $"{task.Id} {task.Target}: {(outcome.Success ? outcome.Summary : outcome.Error)}");

        return new TaskResult(task.Id, task.File, status, outcome.Summary, outcome.Diff, outcome.Error, stopwatch.ElapsedMilliseconds, outcome.Tokens);
    }
}

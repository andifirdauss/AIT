using System.Diagnostics;
using AIThread.Common.Entities;
using AIThread.Common.Enums;
using AIThread.Service.Models;

namespace AIThread.Service.Orchestration;

public sealed partial class ExecutionEngine
{
    private async Task<TaskResult> RunTaskAsync(
        PlanTask task,
        ThreadTask entity,
        ExecutionRequest request,
        SemaphoreSlim globalGate,
        TaskLine columns,
        ThreadRecorder recorder,
        ClientProgress progress,
        CancellationToken cancellationToken)
    {
        await globalGate.WaitAsync(cancellationToken);
        try
        {
            var node = PickExecutor(request.Executors);
            using var lease = await limiter.AcquireAsync(node, cancellationToken);

            var stopwatch = Stopwatch.StartNew();
            await recorder.UpdateTaskAsync(entity, e =>
            {
                e.Status = ThreadTaskStatus.Running;
                e.NodeId = node.Id;
                e.NodeName = node.Name;
                e.Model = node.Model;
                e.StartedAt = DateTime.UtcNow;
            });

            var outcome = await executor.ExecuteAsync(new ExecutorJob(
                request.Plan,
                task,
                node,
                request.Scope,
                request.Settings.ExecutorMaxRetries,
                delta =>
                {
                    recorder.TaskOutput(entity, delta);
                    return Task.CompletedTask;
                },
                async attempt =>
                {
                    if (attempt > 1) recorder.TaskOutputReset(entity, $"\n--- attempt {attempt} ---\n");
                    await recorder.UpdateTaskAsync(entity, e => e.Attempts = attempt);
                },
                request.Journal,
                async change =>
                {
                    await change.ReportAsync($"Executor {task.Id}", recorder, progress, cancellationToken);
                    await recorder.UpdateTaskAsync(entity, e =>
                    {
                        e.NodeId = change.To.Id;
                        e.NodeName = change.To.Name;
                        e.Model = change.To.Model;
                    });
                },
                ChatCompletionRequest.CacheKey(recorder.Thread.Id, "executor")), cancellationToken);

            stopwatch.Stop();
            recorder.TaskOutput(entity, string.Empty, flush: true);
            return await FinishAsync(task, entity, outcome, stopwatch, entity.NodeName, columns, recorder, progress, cancellationToken);
        }
        finally
        {
            globalGate.Release();
        }
    }
}

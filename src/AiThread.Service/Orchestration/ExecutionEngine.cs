using System.Diagnostics;
using AiThread.Common.Entities;
using AiThread.Common.Enums;
using AiThread.Service.Models;
using AiThread.Service.Workspace;

namespace AiThread.Service.Orchestration;

public sealed record ExecutionRequest(
    ThreadPlan Plan,
    IReadOnlyList<ThreadTask> TaskEntities,
    IReadOnlyList<AiNode> Executors,
    WorkspaceScope Scope,
    RuntimeSettings Settings,
    // Null when the changes need not be undoable (no thread to undo).
    FileJournal? Journal = null);

public sealed record TaskResult(string TaskKey, string File, ThreadTaskStatus Status, string? Summary, string? Diff, string? Error, long DurationMs, int Tokens);

public sealed record ExecutionSummary(IReadOnlyList<TaskResult> Results, long WallMs, long SumMs, int Tokens);

// Runs the task DAG: every task whose dependencies succeeded starts immediately, so the stage takes as long as
// the slowest chain — not the sum of all tasks. Tasks whose dependency failed are skipped, not attempted.
public sealed class ExecutionEngine(ExecutorAgent executor, NodeConcurrencyLimiter limiter)
{
    private int _roundRobin;

    public async Task<ExecutionSummary> RunAsync(ExecutionRequest request, ThreadRecorder recorder, ClientProgress progress, CancellationToken cancellationToken)
    {
        var entities = request.TaskEntities.ToDictionary(t => t.TaskKey, StringComparer.OrdinalIgnoreCase);
        var results = new Dictionary<string, TaskResult>(StringComparer.OrdinalIgnoreCase);
        var pending = new List<PlanTask>(request.Plan.Tasks);
        var running = new Dictionary<Task<TaskResult>, string>();
        using var globalGate = new SemaphoreSlim(request.Settings.MaxParallelTasks);
        var wall = Stopwatch.StartNew();

        while (pending.Count > 0 || running.Count > 0)
        {
            bool progressed;
            do
            {
                progressed = false;
                foreach (var task in pending.ToList())
                {
                    var dependencies = task.DependsOn ?? [];
                    if (dependencies.Any(d => !results.ContainsKey(d))) continue;

                    pending.Remove(task);
                    progressed = true;

                    var failed = dependencies.Where(d => results[d].Status != ThreadTaskStatus.Succeeded).ToList();
                    if (failed.Count > 0)
                    {
                        results[task.Id] = await SkipAsync(task, entities[task.Id], $"Dependency tidak berhasil: {string.Join(", ", failed)}", recorder, progress, cancellationToken);
                        continue;
                    }

                    running[RunTaskAsync(task, entities[task.Id], request, globalGate, recorder, progress, cancellationToken)] = task.Id;
                }
            } while (progressed);

            if (running.Count == 0)
            {
                // Unreachable after validation (cycles are rejected), but never spin forever.
                foreach (var task in pending)
                    results[task.Id] = await SkipAsync(task, entities[task.Id], "Dependency tidak dapat diselesaikan.", recorder, progress, cancellationToken);
                break;
            }

            var finished = await Task.WhenAny(running.Keys);
            running.Remove(finished);
            var result = await finished;
            results[result.TaskKey] = result;
        }

        wall.Stop();
        var ordered = request.Plan.Tasks.Select(t => results[t.Id]).ToList();
        return new ExecutionSummary(ordered, wall.ElapsedMilliseconds, ordered.Sum(r => r.DurationMs), ordered.Sum(r => r.Tokens));
    }

    private async Task<TaskResult> RunTaskAsync(
        PlanTask task,
        ThreadTask entity,
        ExecutionRequest request,
        SemaphoreSlim globalGate,
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
                }), cancellationToken);

            stopwatch.Stop();
            recorder.TaskOutput(entity, string.Empty, flush: true);

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

            var seconds = stopwatch.Elapsed.TotalSeconds.ToString("0.0");
            await progress.LineAsync(outcome.Success
                ? $"   ✅ {task.Id} `{task.File}` ({seconds}s, {entity.NodeName}) — {outcome.Summary}"
                : $"   ❌ {task.Id} `{task.File}` ({seconds}s, {entity.NodeName}) — {outcome.Error}", cancellationToken);
            recorder.Log(outcome.Success ? ActivityLevel.Success : ActivityLevel.Error,
                $"{task.Id} {task.File}: {(outcome.Success ? outcome.Summary : outcome.Error)}");

            return new TaskResult(task.Id, task.File, status, outcome.Summary, outcome.Diff, outcome.Error, stopwatch.ElapsedMilliseconds, outcome.Tokens);
        }
        finally
        {
            globalGate.Release();
        }
    }

    private static async Task<TaskResult> SkipAsync(PlanTask task, ThreadTask entity, string reason, ThreadRecorder recorder, ClientProgress progress, CancellationToken cancellationToken)
    {
        await recorder.UpdateTaskAsync(entity, e =>
        {
            e.Status = ThreadTaskStatus.Skipped;
            e.Error = reason;
            e.CompletedAt = DateTime.UtcNow;
        });
        await progress.LineAsync($"   ⏭️ {task.Id} `{task.File}` dilewati — {reason}", cancellationToken);
        return new TaskResult(task.Id, task.File, ThreadTaskStatus.Skipped, null, null, reason, 0, 0);
    }

    // Prefer the executor with the most free slots; rotate among equals so load spreads across models.
    private AiNode PickExecutor(IReadOnlyList<AiNode> executors)
    {
        if (executors.Count == 1) return executors[0];
        var start = Interlocked.Increment(ref _roundRobin);
        return executors
            .Select((node, index) => (node, index))
            .OrderByDescending(x => limiter.Available(x.node))
            .ThenBy(x => x.node.Priority)
            .ThenBy(x => (x.index - start % executors.Count + executors.Count) % executors.Count)
            .First().node;
    }
}

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
    FileJournal? Journal = null,
    // Fix tasks of a verification round: their progress lines sit under the round's entry instead of being entries.
    bool Nested = false);

public sealed record TaskResult(string TaskKey, string File, ThreadTaskStatus Status, string? Summary, string? Diff, string? Error, long DurationMs, int Tokens);

public sealed record ExecutionSummary(IReadOnlyList<TaskResult> Results, long WallMs, long SumMs, int Tokens);

// Runs the task DAG: every task whose dependencies succeeded starts immediately, so the stage takes as long as
// the slowest chain — not the sum of all tasks. Tasks whose dependency failed are skipped, not attempted.
// Delete and move tasks are done by FileOperations without a model.
public sealed class ExecutionEngine(ExecutorAgent executor, NodeConcurrencyLimiter limiter, FileOperations fileOperations)
{
    private const string NoModel = "tanpa model";

    private int _roundRobin;

    public async Task<ExecutionSummary> RunAsync(ExecutionRequest request, ThreadRecorder recorder, ClientProgress progress, CancellationToken cancellationToken)
    {
        var entities = request.TaskEntities.ToDictionary(t => t.TaskKey, StringComparer.OrdinalIgnoreCase);
        var results = new Dictionary<string, TaskResult>(StringComparer.OrdinalIgnoreCase);
        var pending = new List<PlanTask>(request.Plan.Tasks);
        var running = new Dictionary<Task<TaskResult>, string>();
        using var globalGate = new SemaphoreSlim(request.Settings.MaxParallelTasks);
        var columns = TaskLine.For(request);
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
                        results[task.Id] = await SkipAsync(task, entities[task.Id], $"Dependency tidak berhasil: {string.Join(", ", failed)}", columns, recorder, progress, cancellationToken);
                        continue;
                    }

                    var run = task.IsFileOperation
                        ? RunFileOperationAsync(task, entities[task.Id], request, columns, recorder, progress, cancellationToken)
                        : RunTaskAsync(task, entities[task.Id], request, globalGate, columns, recorder, progress, cancellationToken);
                    running[run] = task.Id;
                }
            } while (progressed);

            if (running.Count == 0)
            {
                // Unreachable after validation (cycles are rejected), but never spin forever.
                foreach (var task in pending)
                    results[task.Id] = await SkipAsync(task, entities[task.Id], "Dependency tidak dapat diselesaikan.", columns, recorder, progress, cancellationToken);
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

    // One progress line per finished task, in columns sized for the whole batch: icon, id, seconds, executor, text.
    private sealed record TaskLine(int IdWidth, int NodeWidth, bool Nested)
    {
        private const int SecondsWidth = 6;

        public static TaskLine For(ExecutionRequest request) => new(
            request.Plan.Tasks.Select(t => t.Id.Length).DefaultIfEmpty(2).Max(),
            request.Executors.Select(e => ClientProgress.DisplayWidth(e.Name)).Append(NoModel.Length).Max(),
            request.Nested);

        public Task WriteAsync(ClientProgress progress, string icon, string id, string seconds, string node, string text, CancellationToken cancellationToken)
        {
            var head = $"{icon} {ClientProgress.Pad(id, IdWidth)} {ClientProgress.PadLeft(seconds, SecondsWidth)}  {ClientProgress.Pad(node, NodeWidth)}  ";
            var hang = ClientProgress.DisplayWidth(head);
            var line = head + text.ReplaceLineEndings(" ");
            return Nested
                ? progress.DetailAsync(line, cancellationToken, hang: hang)
                : progress.StepAsync(line, cancellationToken, hang: hang);
        }
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

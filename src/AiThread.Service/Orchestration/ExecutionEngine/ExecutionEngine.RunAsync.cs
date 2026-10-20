using System.Diagnostics;
using AiThread.Common.Enums;

namespace AiThread.Service.Orchestration;

public sealed partial class ExecutionEngine
{
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
}

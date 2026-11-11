using System.Diagnostics;
using AIThread.Common.Entities;
using AIThread.Common.Enums;
using AIThread.Common.Localization;

namespace AIThread.Service.Orchestration;

public sealed partial class ExecutionEngine
{
    public async Task<ExecutionSummary> RunAsync(ExecutionRequest request, ThreadRecorder recorder, ClientProgress progress, CancellationToken cancellationToken)
    {
        var feed = request.Feed ?? ExecutionFeed.Of(request.Plan, request.TaskEntities);
        var entities = new Dictionary<string, ThreadTask>(StringComparer.OrdinalIgnoreCase);
        // The plan each task's executor sees: the one of the batch it came in.
        var plans = new Dictionary<string, ThreadPlan>(StringComparer.OrdinalIgnoreCase);
        var results = feed.Seeded.ToDictionary(r => r.TaskKey, StringComparer.OrdinalIgnoreCase);
        var order = feed.Seeded.Select(r => r.TaskKey).ToList();
        var pending = new List<PlanTask>();
        var running = new Dictionary<Task<TaskResult>, string>();
        using var globalGate = new SemaphoreSlim(request.Settings.MaxParallelTasks);
        var columns = TaskLine.For(request);
        var wall = Stopwatch.StartNew();
        Task<bool>? more = feed.Reader.WaitToReadAsync(cancellationToken).AsTask();

        while (true)
        {
            if (more is { IsCompleted: true })
            {
                var open = await more;
                while (feed.Reader.TryRead(out var batch))
                {
                    var byKey = batch.Entities.ToDictionary(e => e.TaskKey, StringComparer.OrdinalIgnoreCase);
                    foreach (var task in batch.Tasks)
                    {
                        entities[task.Id] = byKey[task.Id];
                        plans[task.Id] = batch.Plan;
                        pending.Add(task);
                        order.Add(task.Id);
                    }
                }
                more = open ? feed.Reader.WaitToReadAsync(cancellationToken).AsTask() : null;
                if (more is { IsCompleted: true }) continue;
            }

            bool progressed;
            do
            {
                progressed = false;
                foreach (var task in pending.ToList())
                {
                    var dependencies = task.DependsOn ?? [];
                    // After: earlier tasks on the same file only have to be over, whatever their result.
                    if (dependencies.Concat(task.After ?? []).Any(d => !results.ContainsKey(d))) continue;

                    pending.Remove(task);
                    progressed = true;

                    var failed = dependencies.Where(d => results[d].Status != ThreadTaskStatus.Succeeded).ToList();
                    if (failed.Count > 0)
                    {
                        var skipped = await SkipAsync(task, entities[task.Id], Lang.T($"Dependency tidak berhasil: {string.Join(", ", failed)}", $"Dependency did not succeed: {string.Join(", ", failed)}"), columns, recorder, progress, cancellationToken);
                        results[task.Id] = skipped;
                        feed.Report(skipped);
                        continue;
                    }

                    var run = task.IsFileOperation
                        ? RunFileOperationAsync(task, entities[task.Id], request, columns, recorder, progress, cancellationToken)
                        : RunTaskAsync(task, plans[task.Id], entities[task.Id], request, globalGate, columns, recorder, progress, cancellationToken);
                    running[run] = task.Id;
                }
            } while (progressed);

            if (running.Count == 0 && more is null)
            {
                // Unreachable after validation (cycles are rejected), but never spin forever.
                foreach (var task in pending)
                    results[task.Id] = await SkipAsync(task, entities[task.Id], Lang.T("Dependency tidak dapat diselesaikan.", "Dependency cannot be resolved."), columns, recorder, progress, cancellationToken);
                break;
            }

            // Whichever comes first: a task ending, or the next batch.
            var waits = running.Keys.Cast<Task>().ToList();
            if (more is not null) waits.Add(more);
            var finished = await Task.WhenAny(waits);
            if (ReferenceEquals(finished, more)) continue;

            var done = (Task<TaskResult>)finished;
            running.Remove(done);
            var result = await done;
            results[result.TaskKey] = result;
            feed.Report(result);
        }

        wall.Stop();
        var ordered = order.Select(id => results[id]).ToList();
        return new ExecutionSummary(ordered, wall.ElapsedMilliseconds, ordered.Sum(r => r.DurationMs), ordered.Sum(r => r.Tokens));
    }
}

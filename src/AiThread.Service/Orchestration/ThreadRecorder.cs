using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using AiThread.Common.DTOs;
using AiThread.Common.Entities;
using AiThread.Common.Enums;
using AiThread.Data.Context;
using AiThread.Data.Messaging;
using AiThread.Service.Implementations;
using AiThread.Service.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace AiThread.Service.Orchestration;

// Persists thread/task state and mirrors every change to the SSE bus. Writes use CancellationToken.None
// on purpose: a cancelled thread must still record that it was cancelled.
public sealed class ThreadRecorder(
    IDbContextFactory<AiThreadDbContext> dbFactory,
    IEventBus eventBus,
    IActivityLogService activityLog,
    ThreadRun thread)
{
    private readonly SemaphoreSlim _threadLock = new(1, 1);
    private readonly ConcurrentDictionary<Guid, OutputBuffer> _outputBuffers = new();

    public ThreadRun Thread => thread;

    public async Task UpdateThreadAsync(Action<ThreadRun> mutate)
    {
        await _threadLock.WaitAsync();
        try
        {
            mutate(thread);
            thread.UpdatedAt = DateTime.UtcNow;
            await using var db = await dbFactory.CreateDbContextAsync();
            db.Threads.Update(thread);
            await db.SaveChangesAsync();
        }
        finally
        {
            _threadLock.Release();
        }

        eventBus.Publish(AppEvent.Create(AppEventTypes.ThreadUpdated, new
        {
            thread.Id,
            thread.Status,
            thread.PlanSummary,
            thread.TotalMs,
            thread.Error
        }, thread.Id));
    }

    // firstOrder: fix tasks of the verification stage continue after the planned ones.
    public async Task<IReadOnlyList<ThreadTask>> CreateTasksAsync(ThreadPlan plan, int firstOrder = 1)
    {
        var tasks = plan.Tasks.Select((t, i) => new ThreadTask
        {
            ThreadRunId = thread.Id,
            TaskKey = Truncate(t.Id, 20),
            Order = firstOrder + i,
            Title = Truncate(t.Title, 300),
            FilePath = Truncate(t.File, 800),
            Action = t.Action,
            TargetPath = t.To is null ? null : Truncate(t.To, 800),
            Instruction = t.Instruction,
            DependsOn = t.DependsOn is { Count: > 0 } deps ? Truncate(string.Join(",", deps), 300) : null,
            Status = ThreadTaskStatus.Pending
        }).ToList();

        await using var db = await dbFactory.CreateDbContextAsync();
        db.ThreadTasks.AddRange(tasks);
        await db.SaveChangesAsync();

        foreach (var task in tasks) PublishTask(task);
        return tasks;
    }

    public async Task UpdateTaskAsync(ThreadTask task, Action<ThreadTask> mutate)
    {
        lock (task) mutate(task);
        task.UpdatedAt = DateTime.UtcNow;

        await using var db = await dbFactory.CreateDbContextAsync();
        db.ThreadTasks.Update(task);
        await db.SaveChangesAsync();
        PublishTask(task);
    }

    // Live model output for the task panel, batched to keep the event stream light.
    public void TaskOutput(ThreadTask task, string delta, bool flush = false)
    {
        var buffer = _outputBuffers.GetOrAdd(task.Id, _ => new OutputBuffer());
        string? chunk;
        lock (buffer)
        {
            buffer.Text.Append(delta);
            var due = flush || buffer.Text.Length >= 200 || buffer.SinceFlush.ElapsedMilliseconds >= 300;
            if (!due || buffer.Text.Length == 0) return;
            chunk = buffer.Text.ToString();
            buffer.Text.Clear();
            buffer.SinceFlush.Restart();
        }

        eventBus.Publish(AppEvent.Create(AppEventTypes.TaskOutput, new { taskId = task.Id, taskKey = task.TaskKey, text = chunk }, thread.Id));
    }

    public void TaskOutputReset(ThreadTask task, string header) =>
        eventBus.Publish(AppEvent.Create(AppEventTypes.TaskOutput, new { taskId = task.Id, taskKey = task.TaskKey, text = header, reset = true }, thread.Id));

    public void PlannerActivity(string tool, string detail) =>
        eventBus.Publish(AppEvent.Create(AppEventTypes.ThreadOutput, new { kind = "planner.tool", tool, detail }, thread.Id));

    public void Log(ActivityLevel level, string message) =>
        activityLog.Write(level, "thread", message, thread.Id, thread.ProjectId);

    private void PublishTask(ThreadTask task) =>
        eventBus.Publish(AppEvent.Create(AppEventTypes.TaskUpdated, ThreadService.ToTaskDto(task), thread.Id));

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];

    private sealed class OutputBuffer
    {
        public StringBuilder Text { get; } = new();
        public Stopwatch SinceFlush { get; } = Stopwatch.StartNew();
    }
}

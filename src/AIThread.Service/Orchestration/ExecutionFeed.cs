using System.Collections.Concurrent;
using System.Threading.Channels;
using AIThread.Common.Entities;

namespace AIThread.Service.Orchestration;

// One batch for the execution engine: its tasks with their entities, and the plan their executors see (the summary
// and the contract as they were when the batch was committed).
public sealed record FeedBatch(ThreadPlan Plan, IReadOnlyList<PlanTask> Tasks, IReadOnlyList<ThreadTask> Entities);

// The tasks of one execution run, arriving while it runs: the batches the planner commits (commit_tasks), then the
// rest of the final plan, until Complete. Seeded results are tasks an earlier run of the same thread finished (before
// the planner paused for a question): later tasks may depend on them. Every task the engine finishes is also kept for
// the planner (TakeFinished), which reads how its committed tasks went.
public sealed class ExecutionFeed(IReadOnlyList<TaskResult>? seeded = null)
{
    private readonly Channel<FeedBatch> _batches = Channel.CreateUnbounded<FeedBatch>();
    private readonly ConcurrentQueue<TaskResult> _finished = new();

    public IReadOnlyList<TaskResult> Seeded { get; } = seeded ?? [];

    public ChannelReader<FeedBatch> Reader => _batches.Reader;

    // A feed that holds one plan and nothing more: a run that starts once its tasks are all known.
    public static ExecutionFeed Of(ThreadPlan plan, IReadOnlyList<ThreadTask> entities)
    {
        var feed = new ExecutionFeed();
        feed.Add(new FeedBatch(plan, plan.Tasks, entities));
        feed.Complete();
        return feed;
    }

    public void Add(FeedBatch batch)
    {
        if (!_batches.Writer.TryWrite(batch)) throw new InvalidOperationException("The execution feed is already complete.");
    }

    // No more batches: the run ends once the tasks it has are done.
    public void Complete() => _batches.Writer.TryComplete();

    public void Report(TaskResult result) => _finished.Enqueue(result);

    // The tasks finished since the last call, in the order they ended.
    public IReadOnlyList<TaskResult> TakeFinished()
    {
        var taken = new List<TaskResult>();
        while (_finished.TryDequeue(out var result)) taken.Add(result);
        return taken;
    }
}

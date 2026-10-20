using AIThread.Common.Entities;

namespace AIThread.Service.Orchestration;

public sealed partial class ExecutionEngine
{
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

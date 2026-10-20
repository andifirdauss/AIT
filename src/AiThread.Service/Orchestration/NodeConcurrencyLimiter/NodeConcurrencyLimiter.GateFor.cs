using AiThread.Common.Entities;

namespace AiThread.Service.Orchestration;

public sealed partial class NodeConcurrencyLimiter
{
    // A changed MaxConcurrency gets a fresh gate; leases on the old gate release into it harmlessly.
    private Gate GateFor(AiNode node)
    {
        var capacity = Math.Max(1, node.MaxConcurrency);
        return _gates.AddOrUpdate(
            node.Id,
            _ => new Gate(capacity, new SemaphoreSlim(capacity, capacity)),
            (_, existing) => existing.Capacity == capacity ? existing : new Gate(capacity, new SemaphoreSlim(capacity, capacity)));
    }
}

using System.Collections.Concurrent;
using AiThread.Common.Entities;

namespace AiThread.Service.Orchestration;

// Enforces AiNode.MaxConcurrency across every running thread (several projects can use the proxy at once).
public sealed class NodeConcurrencyLimiter
{
    private readonly ConcurrentDictionary<Guid, Gate> _gates = new();

    public async Task<IDisposable> AcquireAsync(AiNode node, CancellationToken cancellationToken)
    {
        var gate = GateFor(node);
        await gate.Semaphore.WaitAsync(cancellationToken);
        return new Lease(gate.Semaphore);
    }

    public int Available(AiNode node) => GateFor(node).Semaphore.CurrentCount;

    // A changed MaxConcurrency gets a fresh gate; leases on the old gate release into it harmlessly.
    private Gate GateFor(AiNode node)
    {
        var capacity = Math.Max(1, node.MaxConcurrency);
        return _gates.AddOrUpdate(
            node.Id,
            _ => new Gate(capacity, new SemaphoreSlim(capacity, capacity)),
            (_, existing) => existing.Capacity == capacity ? existing : new Gate(capacity, new SemaphoreSlim(capacity, capacity)));
    }

    private sealed record Gate(int Capacity, SemaphoreSlim Semaphore);

    private sealed class Lease(SemaphoreSlim semaphore) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0) semaphore.Release();
        }
    }
}

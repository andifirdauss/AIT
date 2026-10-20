using System.Collections.Concurrent;
using AiThread.Service.Interfaces;

namespace AiThread.Service.Orchestration;

// Enforces AiNode.MaxConcurrency across every running thread (several projects can use the proxy at once).
public sealed partial class NodeConcurrencyLimiter : INodeConcurrencyLimiter
{
    private readonly ConcurrentDictionary<Guid, Gate> _gates = new();

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

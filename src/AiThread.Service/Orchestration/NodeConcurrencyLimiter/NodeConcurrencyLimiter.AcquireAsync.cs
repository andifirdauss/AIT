using AiThread.Common.Entities;

namespace AiThread.Service.Orchestration;

public sealed partial class NodeConcurrencyLimiter
{
    public async Task<IDisposable> AcquireAsync(AiNode node, CancellationToken cancellationToken)
    {
        var gate = GateFor(node);
        await gate.Semaphore.WaitAsync(cancellationToken);
        return new Lease(gate.Semaphore);
    }
}

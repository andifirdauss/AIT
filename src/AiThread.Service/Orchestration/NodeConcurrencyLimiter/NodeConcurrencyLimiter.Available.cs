using AiThread.Common.Entities;

namespace AiThread.Service.Orchestration;

public sealed partial class NodeConcurrencyLimiter
{
    public int Available(AiNode node) => GateFor(node).Semaphore.CurrentCount;
}

using AIThread.Common.Entities;

namespace AIThread.Service.Orchestration;

public sealed partial class NodeConcurrencyLimiter
{
    public int Available(AiNode node) => GateFor(node).Semaphore.CurrentCount;
}

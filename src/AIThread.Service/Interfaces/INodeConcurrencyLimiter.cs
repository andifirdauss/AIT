using AIThread.Common.Entities;

namespace AIThread.Service.Interfaces;

public interface INodeConcurrencyLimiter
{
    Task<IDisposable> AcquireAsync(AiNode node, CancellationToken cancellationToken);

    int Available(AiNode node);
}

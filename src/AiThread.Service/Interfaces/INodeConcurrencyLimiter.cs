using AiThread.Common.Entities;

namespace AiThread.Service.Interfaces;

public interface INodeConcurrencyLimiter
{
    Task<IDisposable> AcquireAsync(AiNode node, CancellationToken cancellationToken);

    int Available(AiNode node);
}

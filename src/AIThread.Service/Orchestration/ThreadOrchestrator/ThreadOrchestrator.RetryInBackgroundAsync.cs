using AIThread.Service.Models;

namespace AIThread.Service.Orchestration;

public sealed partial class ThreadOrchestrator
{
    public async Task<Guid> RetryInBackgroundAsync(Guid threadId, CancellationToken cancellationToken)
    {
        var prepared = await PrepareRetryAsync(threadId, lifetime.ApplicationStopping, cancellationToken);
        _ = Task.Run(() => ExecuteRetryAsync(prepared, NullThreadObserver.Instance), CancellationToken.None);
        return threadId;
    }
}

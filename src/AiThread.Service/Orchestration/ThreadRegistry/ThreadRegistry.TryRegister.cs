namespace AiThread.Service.Orchestration;

public sealed partial class ThreadRegistry
{
    // Null when the thread is already registered: a retry or rollback must not start while it runs.
    public CancellationTokenSource? TryRegister(Guid threadId, CancellationToken externalToken)
    {
        var source = CancellationTokenSource.CreateLinkedTokenSource(externalToken);
        if (_running.TryAdd(threadId, source)) return source;
        source.Dispose();
        return null;
    }
}

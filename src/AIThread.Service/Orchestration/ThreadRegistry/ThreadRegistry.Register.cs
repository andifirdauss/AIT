namespace AIThread.Service.Orchestration;

public sealed partial class ThreadRegistry
{
    public CancellationTokenSource Register(Guid threadId, CancellationToken externalToken)
    {
        var source = CancellationTokenSource.CreateLinkedTokenSource(externalToken);
        _running[threadId] = source;
        return source;
    }
}

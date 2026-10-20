namespace AiThread.Service.Interfaces;

public interface IThreadRegistry
{
    int ActiveCount { get; }

    CancellationTokenSource Register(Guid threadId, CancellationToken externalToken);

    CancellationTokenSource? TryRegister(Guid threadId, CancellationToken externalToken);

    bool IsRunning(Guid threadId);

    bool Cancel(Guid threadId);

    void Complete(Guid threadId);

    int CancelAll();

    Task<bool> WaitForIdleAsync(TimeSpan timeout, CancellationToken cancellationToken);
}

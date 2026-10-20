namespace AiThread.Service.Orchestration;

public sealed partial class ThreadRegistry
{
    // True once no thread is registered; false when the timeout elapses first.
    public async Task<bool> WaitForIdleAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!_running.IsEmpty)
        {
            if (DateTime.UtcNow >= deadline) return false;
            await Task.Delay(100, cancellationToken);
        }
        return true;
    }
}

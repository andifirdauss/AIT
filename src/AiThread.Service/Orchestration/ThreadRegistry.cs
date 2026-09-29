using System.Collections.Concurrent;

namespace AiThread.Service.Orchestration;

// Running threads and their cancellation handles (cancel button, dashboard "active" counter).
public sealed class ThreadRegistry
{
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _running = new();

    public int ActiveCount => _running.Count;

    public CancellationTokenSource Register(Guid threadId, CancellationToken externalToken)
    {
        var source = CancellationTokenSource.CreateLinkedTokenSource(externalToken);
        _running[threadId] = source;
        return source;
    }

    public bool Cancel(Guid threadId)
    {
        if (!_running.TryGetValue(threadId, out var source)) return false;
        try
        {
            source.Cancel();
        }
        catch (ObjectDisposedException)
        {
            return false; // Completed in the meantime.
        }
        return true;
    }

    public void Complete(Guid threadId)
    {
        if (_running.TryRemove(threadId, out var source)) source.Dispose();
    }

    public int CancelAll()
    {
        var cancelled = 0;
        foreach (var threadId in _running.Keys)
            if (Cancel(threadId)) cancelled++;
        return cancelled;
    }

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

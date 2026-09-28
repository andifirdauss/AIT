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
        source.Cancel();
        return true;
    }

    public void Complete(Guid threadId)
    {
        if (_running.TryRemove(threadId, out var source)) source.Dispose();
    }
}

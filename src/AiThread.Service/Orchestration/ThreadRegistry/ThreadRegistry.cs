using System.Collections.Concurrent;
using AiThread.Service.Interfaces;

namespace AiThread.Service.Orchestration;

// Running threads and their cancellation handles (cancel button, dashboard "active" counter).
public sealed partial class ThreadRegistry : IThreadRegistry
{
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _running = new();

    public int ActiveCount => _running.Count;
}

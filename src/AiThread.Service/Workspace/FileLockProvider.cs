using System.Collections.Concurrent;

namespace AiThread.Service.Workspace;

// Executors run in parallel, but writes to one file are serialized. Each writer re-reads the file
// inside the lock, so non-overlapping edits from different tasks on the same file both land.
public sealed class FileLockProvider
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.OrdinalIgnoreCase);

    public async Task<IDisposable> AcquireAsync(string fullPath, CancellationToken cancellationToken)
    {
        var gate = _locks.GetOrAdd(Path.GetFullPath(fullPath), _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        return new Releaser(gate);
    }

    private sealed class Releaser(SemaphoreSlim gate) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0) gate.Release();
        }
    }
}

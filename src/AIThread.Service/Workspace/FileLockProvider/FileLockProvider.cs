using System.Collections.Concurrent;
using AIThread.Service.Interfaces;

namespace AIThread.Service.Workspace;

// Executors run in parallel, but writes to one file are serialized. Each writer re-reads the file
// inside the lock, so non-overlapping edits from different tasks on the same file both land.
public sealed partial class FileLockProvider : IFileLockProvider
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.OrdinalIgnoreCase);

    private sealed class Releaser(SemaphoreSlim gate) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0) gate.Release();
        }
    }
}

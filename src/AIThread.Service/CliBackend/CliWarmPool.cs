using System.Diagnostics;

namespace AIThread.Service.CliBackend;

// A started CLI waiting on stdin for its prompt, and the working folder it runs in.
public sealed record WarmCliProcess(Process Process, string WorkFolder);

// CLI processes started ahead of the call that will use them, so that call does not wait for the CLI to start. One per
// key at most; the oldest goes when the pool is full, and one that nobody took within maxIdle is stopped. They spend
// nothing while they wait: a request only leaves once they get their prompt.
public sealed class CliWarmPool(int capacity, TimeSpan maxIdle, Action<WarmCliProcess> discard) : IDisposable
{
    private readonly object _gate = new();
    private readonly List<Entry> _entries = [];
    private bool _disposed;

    public bool Has(string key)
    {
        lock (_gate) return _entries.Any(e => e.Key == key);
    }

    // Null when none is ready: the caller starts its own.
    public WarmCliProcess? Take(string key)
    {
        Entry? entry;
        lock (_gate)
        {
            entry = _entries.FirstOrDefault(e => e.Key == key);
            if (entry is null) return null;
            _entries.Remove(entry);
        }
        entry.Expiry?.Dispose();
        if (!entry.Warm.Process.HasExited) return entry.Warm;
        discard(entry.Warm);
        return null;
    }

    public void Add(string key, WarmCliProcess warm)
    {
        Entry? evicted = null;
        var kept = false;
        lock (_gate)
        {
            if (!_disposed && capacity > 0 && _entries.All(e => e.Key != key))
            {
                if (_entries.Count >= capacity)
                {
                    evicted = _entries[0];
                    _entries.RemoveAt(0);
                }
                var entry = new Entry(key, warm);
                _entries.Add(entry);
                entry.Expiry = new Timer(_ => Expire(entry), null, maxIdle, Timeout.InfiniteTimeSpan);
                kept = true;
            }
        }
        if (evicted is not null) Drop(evicted);
        if (!kept) discard(warm);
    }

    public void Dispose()
    {
        List<Entry> entries;
        lock (_gate)
        {
            _disposed = true;
            entries = [.. _entries];
            _entries.Clear();
        }
        foreach (var entry in entries) Drop(entry);
    }

    private void Expire(Entry entry)
    {
        lock (_gate)
        {
            if (!_entries.Remove(entry)) return;
        }
        Drop(entry);
    }

    private void Drop(Entry entry)
    {
        entry.Expiry?.Dispose();
        discard(entry.Warm);
    }

    private sealed class Entry(string key, WarmCliProcess warm)
    {
        public string Key { get; } = key;
        public WarmCliProcess Warm { get; } = warm;
        public Timer? Expiry { get; set; }
    }
}

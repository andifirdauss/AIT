namespace AIThread.Service.CliBackend;

// The processes one CLI may run at once on this machine. Work started inside Background() (the background workers)
// takes a background slot first and may hold at most `background` of them, so a thread's planner, executors and tools
// node never queue behind commit summaries or wiki updates. With a single slot there is nothing to keep free.
// A call made inside Priority() (the planner's turns) gets the next free slot before the calls already waiting, so a
// planner that keeps planning while its committed tasks run (commit_tasks) does not queue behind its own executors.
public sealed class CliSlots
{
    private static readonly AsyncLocal<bool> InBackground = new();
    private static readonly AsyncLocal<bool> InPriority = new();

    private readonly object _gate = new();
    private readonly LinkedList<TaskCompletionSource> _priority = new();
    private readonly LinkedList<TaskCompletionSource> _normal = new();
    private readonly SemaphoreSlim _background;
    private int _free;

    public CliSlots(int total, int background)
    {
        var all = Math.Max(1, total);
        _free = all;
        _background = new SemaphoreSlim(Math.Clamp(background, 1, Math.Max(1, all - 1)));
    }

    public static bool IsBackground => InBackground.Value;

    public static bool IsPriority => InPriority.Value;

    // Marks every CLI call of the calling flow (and of what it awaits or starts) as background work until disposed.
    public static IDisposable Background() => new Scope(InBackground, true);

    // Puts the CLI calls of the calling flow ahead of the waiting ones until disposed.
    public static IDisposable Priority() => new Scope(InPriority, true);

    // The slot is given back when the returned handle is disposed.
    public async Task<IDisposable> AcquireAsync(CancellationToken cancellationToken)
    {
        var background = IsBackground;
        if (background) await _background.WaitAsync(cancellationToken);
        try
        {
            await TakeAsync(IsPriority && !background, cancellationToken);
        }
        catch
        {
            if (background) _background.Release();
            throw;
        }
        return new Lease(this, background);
    }

    private Task TakeAsync(bool priority, CancellationToken cancellationToken)
    {
        TaskCompletionSource waiter;
        LinkedListNode<TaskCompletionSource> node;
        lock (_gate)
        {
            // A freed slot goes straight to a waiter, so free slots and waiters never exist together.
            if (_free > 0)
            {
                _free--;
                return Task.CompletedTask;
            }
            waiter = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            node = (priority ? _priority : _normal).AddLast(waiter);
        }
        if (!cancellationToken.CanBeCanceled) return waiter.Task;

        var registration = cancellationToken.Register(() =>
        {
            // Whoever takes the waiter out of its queue decides: the release gave it a slot, or the cancellation ends it.
            lock (_gate)
            {
                if (node.List is null) return;
                node.List.Remove(node);
            }
            waiter.TrySetCanceled(cancellationToken);
        });
        return WaitAsync(waiter.Task, registration);
    }

    private static async Task WaitAsync(Task waiting, CancellationTokenRegistration registration)
    {
        try
        {
            await waiting;
        }
        finally
        {
            await registration.DisposeAsync();
        }
    }

    private void Release()
    {
        TaskCompletionSource? next;
        lock (_gate)
        {
            var queue = _priority.Count > 0 ? _priority : _normal.Count > 0 ? _normal : null;
            if (queue is null)
            {
                _free++;
                return;
            }
            next = queue.First!.Value;
            queue.RemoveFirst();
        }
        next.TrySetResult();
    }

    private sealed class Lease(CliSlots slots, bool background) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) != 0) return;
            slots.Release();
            if (background) slots._background.Release();
        }
    }

    private sealed class Scope : IDisposable
    {
        private readonly AsyncLocal<bool> _flag;
        private readonly bool _previous;

        public Scope(AsyncLocal<bool> flag, bool value)
        {
            _flag = flag;
            _previous = flag.Value;
            flag.Value = value;
        }

        public void Dispose() => _flag.Value = _previous;
    }
}

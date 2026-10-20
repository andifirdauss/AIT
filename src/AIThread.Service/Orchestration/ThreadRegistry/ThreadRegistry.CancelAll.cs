namespace AIThread.Service.Orchestration;

public sealed partial class ThreadRegistry
{
    public int CancelAll()
    {
        var cancelled = 0;
        foreach (var threadId in _running.Keys)
            if (Cancel(threadId)) cancelled++;
        return cancelled;
    }
}

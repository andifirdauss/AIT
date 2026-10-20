namespace AIThread.Service.Orchestration;

public sealed partial class ThreadRegistry
{
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
}

namespace AiThread.Service.Orchestration;

public sealed partial class ThreadRegistry
{
    public void Complete(Guid threadId)
    {
        if (_running.TryRemove(threadId, out var source)) source.Dispose();
    }
}

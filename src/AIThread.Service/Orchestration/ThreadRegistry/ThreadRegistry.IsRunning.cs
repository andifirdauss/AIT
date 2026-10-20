namespace AIThread.Service.Orchestration;

public sealed partial class ThreadRegistry
{
    public bool IsRunning(Guid threadId) => _running.ContainsKey(threadId);
}

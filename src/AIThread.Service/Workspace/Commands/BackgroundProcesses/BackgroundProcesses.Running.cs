namespace AIThread.Service.Workspace.Commands;

public sealed partial class BackgroundProcesses
{
    public int Running(Guid threadId) => _processes.Values.Count(e => e.ThreadId == threadId && e.Running);
}

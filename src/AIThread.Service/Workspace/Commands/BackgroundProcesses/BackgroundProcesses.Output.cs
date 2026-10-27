namespace AIThread.Service.Workspace.Commands;

public sealed partial class BackgroundProcesses
{
    // Null for an id this thread did not start (or already stopped).
    public BackgroundOutput? Output(Guid threadId, string id, int tailLines) =>
        _processes.TryGetValue(id.Trim(), out var entry) && entry.ThreadId == threadId ? Snapshot(entry, tailLines) : null;
}

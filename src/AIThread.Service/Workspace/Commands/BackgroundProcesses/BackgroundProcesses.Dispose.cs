namespace AIThread.Service.Workspace.Commands;

public sealed partial class BackgroundProcesses
{
    // AIT stops: no dev server it started outlives it.
    public void Dispose()
    {
        foreach (var id in _processes.Keys.ToList())
            if (_processes.TryRemove(id, out var entry))
                entry.Dispose();
    }
}

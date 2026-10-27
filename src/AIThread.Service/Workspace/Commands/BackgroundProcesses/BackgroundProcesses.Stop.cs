using Microsoft.Extensions.Logging;

namespace AIThread.Service.Workspace.Commands;

public sealed partial class BackgroundProcesses
{
    // Stops the process tree and forgets it; the result holds its last output. Null for an id of another thread.
    public BackgroundOutput? Stop(Guid threadId, string id, int tailLines)
    {
        if (!_processes.TryGetValue(id.Trim(), out var entry) || entry.ThreadId != threadId || !_processes.TryRemove(entry.Id, out _)) return null;
        var running = entry.Running;
        CommandRunner.Kill(entry.Process);
        entry.Process.WaitForExit(2000);
        var snapshot = Snapshot(entry, tailLines) with { Running = running };
        entry.Dispose();
        logger.LogInformation("Background process {Id} of thread {ThreadId} stopped", entry.Id, threadId);
        return snapshot;
    }
}

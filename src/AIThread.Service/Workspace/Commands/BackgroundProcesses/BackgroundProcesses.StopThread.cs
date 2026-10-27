using Microsoft.Extensions.Logging;

namespace AIThread.Service.Workspace.Commands;

public sealed partial class BackgroundProcesses
{
    // Every process of the thread is stopped and forgotten; returns how many were still running.
    public int StopThread(Guid threadId)
    {
        var stopped = 0;
        foreach (var entry in _processes.Values.Where(e => e.ThreadId == threadId).ToList())
        {
            if (!_processes.TryRemove(entry.Id, out _)) continue;
            if (entry.Running) stopped++;
            entry.Dispose();
        }
        if (stopped > 0) logger.LogInformation("Stopped {Count} background process(es) of thread {ThreadId}", stopped, threadId);
        return stopped;
    }
}

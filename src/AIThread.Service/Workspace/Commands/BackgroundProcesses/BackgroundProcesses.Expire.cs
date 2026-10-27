using Microsoft.Extensions.Logging;

namespace AIThread.Service.Workspace.Commands;

public sealed partial class BackgroundProcesses
{
    // MaxAge passed: the process stops, its output stays readable until the thread ends.
    private void Expire(Entry entry)
    {
        if (!entry.Running) return;
        entry.Expired = true;
        CommandRunner.Kill(entry.Process);
        logger.LogWarning("Background process {Id} of thread {ThreadId} stopped after {Minutes} minutes", entry.Id, entry.ThreadId, MaxAge.TotalMinutes);
    }
}

using AIThread.Service.Workspace.Commands;

namespace AIThread.Service.Interfaces;

// The planner's background processes (start_process, process_output, stop_process), per thread.
public interface IBackgroundProcesses
{
    Task<BackgroundStart> StartAsync(Guid threadId, string command, string workingDirectory, BackgroundWait wait, CancellationToken cancellationToken = default);

    BackgroundOutput? Output(Guid threadId, string id, int tailLines);

    BackgroundOutput? Stop(Guid threadId, string id, int tailLines);

    int Running(Guid threadId);

    int StopThread(Guid threadId);
}

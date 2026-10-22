using AIThread.Service.Workspace;

namespace AIThread.Service.Orchestration;

public sealed partial class ExecutorAgent
{
    private async Task<string> CurrentContentSectionAsync(ExecutorJob job, string fullPath, CancellationToken cancellationToken) =>
        FileSection(job.Task, await TextFileIo.ReadAsync(fullPath, cancellationToken), job.Scope);
}

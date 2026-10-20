using AiThread.Service.Orchestration;
using AiThread.Service.Workspace;

namespace AiThread.Service.Interfaces;

public interface IFileOperations
{
    Task<ExecutorOutcome> RunAsync(PlanTask task, WorkspaceScope scope, FileJournal? journal, CancellationToken cancellationToken);
}

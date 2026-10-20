using AIThread.Service.Orchestration;
using AIThread.Service.Workspace;

namespace AIThread.Service.Interfaces;

public interface IFileOperations
{
    Task<ExecutorOutcome> RunAsync(PlanTask task, WorkspaceScope scope, FileJournal? journal, CancellationToken cancellationToken);
}

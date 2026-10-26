using AIThread.Common.Localization;
using AIThread.Service.Workspace;

namespace AIThread.Service.Orchestration;

public sealed partial class FileOperations
{
    public async Task<ExecutorOutcome> RunAsync(PlanTask task, WorkspaceScope scope, FileJournal? journal, CancellationToken cancellationToken)
    {
        if (!scope.TryResolve(task.File, out var source, out var error)) return Failed(error);
        if (task.IsDelete) return await DeleteAsync(task, scope, source, journal, cancellationToken);

        if (!scope.TryResolve(task.To, out var destination, out error)) return Failed(error ?? Lang.T("Tujuan pemindahan kosong.", "The move target is empty."));
        return task.IsExtract
            ? await ExtractAsync(task, scope, source, destination, journal, cancellationToken)
            : await MoveAsync(task, scope, source, destination, journal, cancellationToken);
    }
}

using AIThread.Service.Workspace;

namespace AIThread.Service.Orchestration;

public sealed partial class FileOperations
{
    private async Task<ExecutorOutcome> DeleteAsync(PlanTask task, WorkspaceScope scope, string source, FileJournal? journal, CancellationToken cancellationToken)
    {
        using var _ = await fileLocks.AcquireAsync(source, cancellationToken);
        if (!File.Exists(source)) return Failed($"'{task.File}' tidak ada (sudah dihapus atau dipindahkan?).");

        if (journal is not null) await journal.BeforeWriteAsync(source);
        File.Delete(source);
        if (journal is not null) await journal.AfterWriteAsync(source);
        PruneEmptyDirectories(Path.GetDirectoryName(source)!, scope.Root);

        var diff = $"--- a/{task.File}\n+++ /dev/null\n@@ file deleted @@\n";
        return new ExecutorOutcome(true, $"Menghapus {task.File}.", diff, null, 0, 1);
    }
}

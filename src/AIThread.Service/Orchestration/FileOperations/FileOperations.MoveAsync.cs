using AIThread.Service.Workspace;

namespace AIThread.Service.Orchestration;

public sealed partial class FileOperations
{
    private async Task<ExecutorOutcome> MoveAsync(PlanTask task, WorkspaceScope scope, string source, string destination, FileJournal? journal, CancellationToken cancellationToken)
    {
        using var leases = await LockPairAsync(source, destination, cancellationToken);

        var target = scope.ToRelative(destination);
        if (!File.Exists(source)) return Failed($"'{task.File}' tidak ada (sudah dihapus atau dipindahkan?).");
        if (File.Exists(destination) || Directory.Exists(destination)) return Failed($"Tujuan '{target}' sudah ada; file tidak dipindahkan.");

        if (journal is not null)
        {
            await journal.BeforeWriteAsync(source);
            await journal.BeforeWriteAsync(destination);
        }
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Move(source, destination);
        if (journal is not null)
        {
            await journal.AfterWriteAsync(source);
            await journal.AfterWriteAsync(destination);
        }
        PruneEmptyDirectories(Path.GetDirectoryName(source)!, scope.Root);

        var diff = $"--- a/{task.File}\n+++ b/{target}\n@@ file moved, content unchanged @@\n";
        return new ExecutorOutcome(true, $"Memindahkan {task.File} ke {target}.", diff, null, 0, 1);
    }
}

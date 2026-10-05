using AiThread.Service.Workspace;

namespace AiThread.Service.Orchestration;

// Delete and move tasks need no model: AiThread does them itself, under the same file locks the executors use, and
// records them in the thread's FileJournal so ThreadRollback can bring the files back. A move keeps the exact bytes;
// changing the content at the new path is a separate edit task that depends on the move.
public sealed class FileOperations(FileLockProvider fileLocks)
{
    public async Task<ExecutorOutcome> RunAsync(PlanTask task, WorkspaceScope scope, FileJournal? journal, CancellationToken cancellationToken)
    {
        if (!scope.TryResolve(task.File, out var source, out var error)) return Failed(error);
        if (!task.IsMove) return await DeleteAsync(task, scope, source, journal, cancellationToken);

        if (!scope.TryResolve(task.To, out var destination, out error)) return Failed(error ?? "Tujuan pemindahan kosong.");
        return await MoveAsync(task, scope, source, destination, journal, cancellationToken);
    }

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

    private async Task<ExecutorOutcome> MoveAsync(PlanTask task, WorkspaceScope scope, string source, string destination, FileJournal? journal, CancellationToken cancellationToken)
    {
        // Fixed lock order, so two moves between the same pair of files cannot deadlock.
        var first = string.Compare(source, destination, StringComparison.OrdinalIgnoreCase) <= 0 ? source : destination;
        using var firstLease = await fileLocks.AcquireAsync(first, cancellationToken);
        using var secondLease = await fileLocks.AcquireAsync(first == source ? destination : source, cancellationToken);

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

    // Removes the folder a file was taken out of, and its parents, while they are empty — never the root itself.
    public static void PruneEmptyDirectories(string directory, string root)
    {
        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var current = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        while (current.Length > fullRoot.Length
               && current.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
               && Directory.Exists(current)
               && !Directory.EnumerateFileSystemEntries(current).Any())
        {
            try
            {
                Directory.Delete(current);
            }
            catch (IOException)
            {
                return;
            }
            catch (UnauthorizedAccessException)
            {
                return;
            }
            current = Path.GetDirectoryName(current)!;
        }
    }

    private static ExecutorOutcome Failed(string? error) => new(false, null, null, error ?? "Path tidak valid.", 0, 1);
}

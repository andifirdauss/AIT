using AiThread.Service.Workspace;

namespace AiThread.Service.Orchestration;

// Delete, move and extract tasks need no model: AiThread does them itself, under the same file locks the executors use,
// and records them in the thread's FileJournal so ThreadRollback can bring the files back. A move keeps the exact bytes;
// changing the content at the new path is a separate edit task that depends on the move. An extract moves C# members
// verbatim into another file (CSharpMemberMover).
public sealed class FileOperations(FileLockProvider fileLocks)
{
    public async Task<ExecutorOutcome> RunAsync(PlanTask task, WorkspaceScope scope, FileJournal? journal, CancellationToken cancellationToken)
    {
        if (!scope.TryResolve(task.File, out var source, out var error)) return Failed(error);
        if (task.IsDelete) return await DeleteAsync(task, scope, source, journal, cancellationToken);

        if (!scope.TryResolve(task.To, out var destination, out error)) return Failed(error ?? "Tujuan pemindahan kosong.");
        return task.IsExtract
            ? await ExtractAsync(task, scope, source, destination, journal, cancellationToken)
            : await MoveAsync(task, scope, source, destination, journal, cancellationToken);
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

    // Both files are read and written under their locks, so extracts from one source into several targets (and executors
    // editing either file) can run in parallel: each one works on the current content.
    private async Task<ExecutorOutcome> ExtractAsync(PlanTask task, WorkspaceScope scope, string source, string destination, FileJournal? journal, CancellationToken cancellationToken)
    {
        using var leases = await LockPairAsync(source, destination, cancellationToken);

        var target = scope.ToRelative(destination);
        if (!File.Exists(source)) return Failed($"'{task.File}' tidak ada (sudah dihapus atau dipindahkan?).");
        if (Directory.Exists(destination)) return Failed($"Tujuan '{target}' adalah folder.");

        var sourceFile = await TextFileIo.ReadAsync(source, cancellationToken);
        var targetFile = await TextFileIo.ReadAsync(destination, cancellationToken);
        MemberMoveResult result;
        try
        {
            result = CSharpMemberMover.Move(sourceFile.Content, targetFile.Exists ? targetFile.Content : null, task.Members ?? []);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return Failed($"Member tidak dapat dipindahkan: {ex.Message}");
        }
        if (!result.Success) return Failed(result.Error);

        if (journal is not null)
        {
            await journal.BeforeWriteAsync(source);
            await journal.BeforeWriteAsync(destination);
        }
        await TextFileIo.WriteAsync(source, result.Source, sourceFile, CancellationToken.None);
        if (result.Target != targetFile.Content || !targetFile.Exists)
            await TextFileIo.WriteAsync(destination, result.Target!, targetFile.Exists ? targetFile : sourceFile, CancellationToken.None);
        if (journal is not null)
        {
            await journal.AfterWriteAsync(source);
            await journal.AfterWriteAsync(destination);
        }

        var summary = new List<string>();
        if (result.Moved.Count > 0) summary.Add($"Memindahkan {string.Join(", ", result.Moved)} dari {task.File} ke {target}{(targetFile.Exists ? string.Empty : " (file baru)")}.");
        if (result.Deduplicated.Count > 0) summary.Add($"Menghapus {string.Join(", ", result.Deduplicated)} dari {task.File} karena sudah ada di {target}.");
        return new ExecutorOutcome(true, string.Join(" ", summary), ExtractDiff(task.File, target, targetFile.Exists, result), null, 0, 1);
    }

    // Signatures only: the bodies did not change, and a full diff of a large split would bury the reviewer.
    private static string ExtractDiff(string source, string target, bool targetExisted, MemberMoveResult result)
    {
        var builder = new System.Text.StringBuilder();
        builder.Append($"--- a/{source}\n+++ b/{source}\n@@ members removed, bodies moved unchanged to {target} @@\n");
        foreach (var signature in result.Signatures) builder.Append($"-{signature}\n");
        if (result.Deduplicated.Count > 0) builder.Append($"@@ already declared in {target}, only removed here: {string.Join(", ", result.Deduplicated)} @@\n");
        if (result.Moved.Count == 0) return builder.ToString();
        builder.Append(targetExisted ? $"--- a/{target}\n" : "--- /dev/null\n").Append($"+++ b/{target}\n@@ members added (partial class part) @@\n");
        foreach (var signature in result.Signatures) builder.Append($"+{signature}\n");
        return builder.ToString();
    }

    // Fixed lock order, so two operations on the same pair of files cannot deadlock.
    private async Task<IDisposable> LockPairAsync(string a, string b, CancellationToken cancellationToken)
    {
        var first = string.Compare(a, b, StringComparison.OrdinalIgnoreCase) <= 0 ? a : b;
        var firstLease = await fileLocks.AcquireAsync(first, cancellationToken);
        try
        {
            var secondLease = await fileLocks.AcquireAsync(first == a ? b : a, cancellationToken);
            return new Leases(firstLease, secondLease);
        }
        catch
        {
            firstLease.Dispose();
            throw;
        }
    }

    private sealed class Leases(IDisposable first, IDisposable second) : IDisposable
    {
        public void Dispose()
        {
            second.Dispose();
            first.Dispose();
        }
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

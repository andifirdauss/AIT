using AiThread.Service.Workspace;

namespace AiThread.Service.Orchestration;

public sealed partial class FileOperations
{
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
}

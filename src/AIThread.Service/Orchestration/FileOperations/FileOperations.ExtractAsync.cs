using AIThread.Common.Localization;
using AIThread.Service.Workspace;

namespace AIThread.Service.Orchestration;

public sealed partial class FileOperations
{
    // Both files are read and written under their locks, so extracts from one source into several targets (and executors
    // editing either file) can run in parallel: each one works on the current content.
    private async Task<ExecutorOutcome> ExtractAsync(PlanTask task, WorkspaceScope scope, string source, string destination, FileJournal? journal, CancellationToken cancellationToken)
    {
        using var leases = await LockPairAsync(source, destination, cancellationToken);

        var target = scope.ToRelative(destination);
        if (!File.Exists(source)) return Failed(Lang.T($"'{task.File}' tidak ada (sudah dihapus atau dipindahkan?).", $"'{task.File}' does not exist (deleted or moved already?)."));
        if (Directory.Exists(destination)) return Failed(Lang.T($"Tujuan '{target}' adalah folder.", $"Target '{target}' is a folder."));

        var sourceFile = await TextFileIo.ReadAsync(source, cancellationToken);
        var targetFile = await TextFileIo.ReadAsync(destination, cancellationToken);
        MemberMoveResult result;
        try
        {
            result = CSharpMemberMover.Move(sourceFile.Content, targetFile.Exists ? targetFile.Content : null, task.Members ?? []);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return Failed(Lang.T($"Member tidak dapat dipindahkan: {ex.Message}", $"Members cannot be moved: {ex.Message}"));
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
        if (result.Moved.Count > 0) summary.Add(Lang.T($"Memindahkan {string.Join(", ", result.Moved)} dari {task.File} ke {target}{(targetFile.Exists ? string.Empty : " (file baru)")}.", $"Moving {string.Join(", ", result.Moved)} from {task.File} to {target}{(targetFile.Exists ? string.Empty : " (new file)")}."));
        if (result.Deduplicated.Count > 0) summary.Add(Lang.T($"Menghapus {string.Join(", ", result.Deduplicated)} dari {task.File} karena sudah ada di {target}.", $"Deleting {string.Join(", ", result.Deduplicated)} from {task.File} because they already exist in {target}."));
        return new ExecutorOutcome(true, string.Join(" ", summary), ExtractDiff(task.File, target, targetFile.Exists, result), null, 0, 1);
    }
}

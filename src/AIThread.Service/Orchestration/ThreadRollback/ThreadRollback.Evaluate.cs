using AIThread.Common.DTOs;
using AIThread.Common.Localization;

namespace AIThread.Service.Orchestration;

public sealed partial class ThreadRollback
{
    private static List<RollbackFileDto> Evaluate(RollbackState state) =>
        state.Snapshots.Select(snapshot =>
        {
            var fullPath = FullPath(state.Root!, snapshot.FilePath);
            var current = File.Exists(fullPath) ? FileJournal.Hash(File.ReadAllBytes(fullPath)) : null;
            if (current is not null && current == snapshot.AfterHash)
                return new RollbackFileDto(snapshot.FilePath, snapshot.Existed ? Restore : Delete, null);
            if (current == snapshot.OriginalHash)
                return new RollbackFileDto(snapshot.FilePath, Unchanged, null);
            // Deleted or moved away by the thread and still absent.
            if (current is null && snapshot.Removed)
                return new RollbackFileDto(snapshot.FilePath, Restore, null);
            return new RollbackFileDto(snapshot.FilePath, Conflict, current is null
                ? Lang.T("File sudah dihapus setelah diubah thread ini.", "The file was deleted after this thread changed it.")
                : Lang.T("Isi file sudah berubah lagi setelah thread ini (diedit developer atau thread lain).", "The file content changed again after this thread (edited by a developer or another thread)."));
        }).ToList();
}

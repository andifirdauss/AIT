using AIThread.Common.DTOs;
using AIThread.Common.Localization;
using AIThread.Service.Models;

namespace AIThread.Service.Orchestration;

public sealed partial class ThreadRollback
{
    public async Task<RollbackResultDto> RollbackAsync(Guid threadId, CancellationToken cancellationToken = default)
    {
        // Holding the registry entry keeps a retry from starting while files are restored.
        using var running = registry.TryRegister(threadId, CancellationToken.None)
                            ?? throw new AppValidationException(Lang.T("Thread ini sedang berjalan; perubahan baru bisa dibatalkan setelah thread selesai.", "This thread is running; its changes can only be rolled back after it finishes."));
        try
        {
            var loaded = await LoadAsync(threadId, cancellationToken) ?? throw new AppValidationException(Lang.T("Thread tidak ditemukan.", "Thread not found."));
            var state = loaded with { Running = false }; // the registry entry is our own
            if (Refusal(state) is { } refusal) throw new AppValidationException(refusal);

            // Lock every file (in a fixed order, so two rollbacks cannot deadlock) before checking any of them: the
            // check and the restore must see the same content.
            var leases = new List<IDisposable>();
            try
            {
                var root = state.Root!; // Refusal rejects a thread without a project
                foreach (var path in state.Snapshots.Select(s => FullPath(root, s.FilePath)).Order(StringComparer.OrdinalIgnoreCase))
                    leases.Add(await fileLocks.AcquireAsync(path, cancellationToken));

                var files = Evaluate(state);
                if (ConflictReason(files) is { } conflict) throw new AppValidationException(conflict);

                foreach (var snapshot in state.Snapshots)
                {
                    var action = files.First(f => f.FilePath == snapshot.FilePath).Action;
                    var fullPath = FullPath(root, snapshot.FilePath);
                    if (action == Restore)
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
                        await File.WriteAllBytesAsync(fullPath, snapshot.OriginalContent ?? [], CancellationToken.None);
                    }
                    else if (action == Delete)
                    {
                        File.Delete(fullPath);
                        FileOperations.PruneEmptyDirectories(Path.GetDirectoryName(fullPath)!, root);
                    }
                }

                await MarkRolledBackAsync(state.Thread, files);
                if (state.Thread.ProjectId is { } projectId && files.Any(f => f.Action is Restore or Delete)) codeStructure.Refresh(projectId);
                return new RollbackResultDto(files.Count(f => f.Action == Restore), files.Count(f => f.Action == Delete), files);
            }
            finally
            {
                foreach (var lease in leases) lease.Dispose();
            }
        }
        finally
        {
            registry.Complete(threadId);
        }
    }
}

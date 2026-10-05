using AiThread.Common.DTOs;
using AiThread.Common.Entities;
using AiThread.Common.Enums;
using AiThread.Data.Context;
using AiThread.Data.Messaging;
using AiThread.Service.Interfaces;
using AiThread.Service.Models;
using AiThread.Service.Workspace;
using Microsoft.EntityFrameworkCore;

namespace AiThread.Service.Orchestration;

// Undoes the file changes of a finished thread from its snapshots (FileJournal). All or nothing: when any file was
// changed again after the thread (by the developer or another thread), nothing is touched and the files are named.
// A file is safe to undo while its content is still exactly what the thread last wrote.
public sealed class ThreadRollback(
    IDbContextFactory<AiThreadDbContext> dbFactory,
    ThreadRegistry registry,
    FileLockProvider fileLocks,
    IEventBus eventBus,
    IActivityLogService activityLog)
{
    public const string Restore = "restore";
    public const string Delete = "delete";
    public const string Unchanged = "unchanged";
    public const string Conflict = "conflict";

    // Null when the thread does not exist.
    public async Task<RollbackPreviewDto?> PreviewAsync(Guid threadId, CancellationToken cancellationToken = default)
    {
        var state = await LoadAsync(threadId, cancellationToken);
        if (state is null) return null;

        var reason = Refusal(state);
        var files = state.Root is null ? [] : Evaluate(state);
        reason ??= ConflictReason(files);
        return new RollbackPreviewDto(reason is null, reason, files);
    }

    public async Task<RollbackResultDto> RollbackAsync(Guid threadId, CancellationToken cancellationToken = default)
    {
        // Holding the registry entry keeps a retry from starting while files are restored.
        using var running = registry.TryRegister(threadId, CancellationToken.None)
                            ?? throw new AppValidationException("Thread ini sedang berjalan; perubahan baru bisa dibatalkan setelah thread selesai.");
        try
        {
            var loaded = await LoadAsync(threadId, cancellationToken) ?? throw new AppValidationException("Thread tidak ditemukan.");
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

    private async Task MarkRolledBackAsync(ThreadRun thread, IReadOnlyList<RollbackFileDto> files)
    {
        var now = DateTime.UtcNow;
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            await db.Threads.Where(t => t.Id == thread.Id).ExecuteUpdateAsync(s => s
                .SetProperty(t => t.Status, ThreadStatus.RolledBack)
                .SetProperty(t => t.RolledBackAt, now)
                .SetProperty(t => t.UpdatedAt, now));
        }

        eventBus.Publish(AppEvent.Create(AppEventTypes.ThreadUpdated, new { thread.Id, Status = ThreadStatus.RolledBack, thread.PlanSummary, thread.TotalMs, thread.Error }, thread.Id));
        var restored = files.Where(f => f.Action == Restore).Select(f => f.FilePath).ToList();
        var deleted = files.Where(f => f.Action == Delete).Select(f => f.FilePath).ToList();
        var parts = new List<string>();
        if (restored.Count > 0) parts.Add($"{restored.Count} file dikembalikan ({string.Join(", ", restored)})");
        if (deleted.Count > 0) parts.Add($"{deleted.Count} file buatan thread dihapus ({string.Join(", ", deleted)})");
        activityLog.Write(ActivityLevel.Warning, "thread", $"Perubahan thread dibatalkan: {(parts.Count == 0 ? "tidak ada file yang perlu dikembalikan" : string.Join("; ", parts))}.",
            thread.Id, thread.ProjectId);
    }

    private async Task<RollbackState?> LoadAsync(Guid threadId, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var thread = await db.Threads.AsNoTracking().Include(t => t.Project).FirstOrDefaultAsync(t => t.Id == threadId, cancellationToken);
        if (thread is null) return null;

        var snapshots = await db.ThreadFileSnapshots.AsNoTracking()
            .Where(s => s.ThreadRunId == threadId)
            .OrderBy(s => s.FilePath)
            .ToListAsync(cancellationToken);
        return new RollbackState(thread, thread.Project?.RootPath, snapshots, registry.IsRunning(threadId));
    }

    private static string? Refusal(RollbackState state) => state switch
    {
        { Thread.Status: ThreadStatus.RolledBack } => "Perubahan thread ini sudah dibatalkan sebelumnya.",
        { Running: true } or { Thread.Status: ThreadStatus.Pending or ThreadStatus.Planning or ThreadStatus.Executing or ThreadStatus.Verifying or ThreadStatus.Wrapping }
            => "Thread ini sedang berjalan; perubahan baru bisa dibatalkan setelah thread selesai.",
        { Root: null } => "Project thread ini sudah dihapus, jadi lokasi file tidak diketahui.",
        { Snapshots.Count: 0 } => "Thread ini tidak mengubah file apa pun (atau dibuat sebelum fitur pembatalan ada).",
        _ => null
    };

    private static string? ConflictReason(IReadOnlyList<RollbackFileDto> files)
    {
        var conflicts = files.Where(f => f.Action == Conflict).ToList();
        if (conflicts.Count > 0)
            return $"Pembatalan ditolak: {conflicts.Count} file sudah berubah setelah thread ini ({string.Join(", ", conflicts.Select(c => c.FilePath))}). " +
                   "Tidak ada file yang disentuh. Kembalikan file itu secara manual (misalnya lewat git) atau batalkan dulu thread yang lebih baru.";
        return files.All(f => f.Action == Unchanged) ? "Semua file sudah sama dengan sebelum thread ini; tidak ada yang perlu dibatalkan." : null;
    }

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
                ? "File sudah dihapus setelah diubah thread ini."
                : "Isi file sudah berubah lagi setelah thread ini (diedit developer atau thread lain).");
        }).ToList();

    // Snapshot paths come from WorkspaceScope.ToRelative, so they stay inside the root; checked again anyway.
    private static string FullPath(string root, string relativePath)
    {
        var fullRoot = Path.GetFullPath(root);
        var fullPath = Path.GetFullPath(Path.Combine(fullRoot, relativePath));
        var prefix = fullRoot.EndsWith(Path.DirectorySeparatorChar) ? fullRoot : fullRoot + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new AppValidationException($"Path '{relativePath}' berada di luar project.");
        return fullPath;
    }

    private sealed record RollbackState(ThreadRun Thread, string? Root, IReadOnlyList<ThreadFileSnapshot> Snapshots, bool Running);
}

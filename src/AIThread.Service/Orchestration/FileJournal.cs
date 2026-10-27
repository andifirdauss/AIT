using System.Collections.Concurrent;
using System.Security.Cryptography;
using AIThread.Common.Entities;
using AIThread.Data.Context;
using AIThread.Service.Workspace;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Orchestration;

// Remembers every file of a thread as it was before the thread first wrote to it (ThreadFileSnapshot), plus the hash of
// what the thread last wrote, so ThreadRollback can undo the thread and recognise files someone changed afterwards.
// In a git repository it also keeps git's blob id of what was written, the fingerprint GitChangeTracker follows; taken
// right away, since the developer may commit or switch branches before the thread is over.
// The executor calls it under the file's lock (FileLockProvider), so one file is never journaled twice at once.
// Writes ignore cancellation: a write that happened must be recorded.
public sealed class FileJournal(IDbContextFactory<AIThreadDbContext> dbFactory, Guid threadId, WorkspaceScope scope)
{
    private readonly ConcurrentDictionary<string, Guid> _snapshots = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lazy<bool> _inRepository = new(() => GitTools.IsRepository(scope.Root));

    public Guid ThreadId => threadId;

    public async Task BeforeWriteAsync(string fullPath)
    {
        // Relative inside the root; absolute for a file outside it the developer allowed.
        var path = scope.ToDisplay(fullPath);
        if (_snapshots.ContainsKey(path)) return;

        await using var db = await dbFactory.CreateDbContextAsync();

        // A retried thread keeps the snapshot of its first run: undoing it returns to the state before the thread.
        var existing = await db.ThreadFileSnapshots
            .Where(s => s.ThreadRunId == threadId && s.FilePath == path)
            .Select(s => (Guid?)s.Id)
            .FirstOrDefaultAsync();
        if (existing is { } id)
        {
            _snapshots[path] = id;
            return;
        }

        var bytes = File.Exists(fullPath) ? await File.ReadAllBytesAsync(fullPath) : null;
        var snapshot = new ThreadFileSnapshot
        {
            ThreadRunId = threadId,
            FilePath = path,
            Existed = bytes is not null,
            OriginalContent = bytes,
            OriginalHash = bytes is null ? null : Hash(bytes)
        };
        db.ThreadFileSnapshots.Add(snapshot);
        await db.SaveChangesAsync();
        _snapshots[path] = snapshot.Id;
    }

    // Also called after a delete or a move away: the file is then gone, and Removed tells ThreadRollback to restore it.
    public async Task AfterWriteAsync(string fullPath)
    {
        if (!_snapshots.TryGetValue(scope.ToDisplay(fullPath), out var id)) return;

        var removed = !File.Exists(fullPath);
        var hash = removed ? null : Hash(await File.ReadAllBytesAsync(fullPath));
        var blob = removed ? null : await BlobAsync(fullPath);
        await using var db = await dbFactory.CreateDbContextAsync();
        await db.ThreadFileSnapshots.Where(s => s.Id == id).ExecuteUpdateAsync(s => s
            .SetProperty(x => x.AfterHash, hash)
            .SetProperty(x => x.AfterBlob, blob)
            .SetProperty(x => x.Removed, removed)
            .SetProperty(x => x.UpdatedAt, DateTime.UtcNow));
    }

    // git hash-object with the repository's filters (.gitattributes, autocrlf), as a commit would store the file; null
    // outside a repository or when git cannot run (GitChangeTracker tries again when the thread is over).
    private async Task<string?> BlobAsync(string fullPath)
    {
        if (!_inRepository.Value || !scope.IsInside(fullPath)) return null;
        var result = await GitProcess.RunAsync(scope.Root, ["hash-object", "--", fullPath], CancellationToken.None, timeout: TimeSpan.FromSeconds(10));
        return result.Ok && result.Output.Trim() is { Length: > 0 and <= 64 } blob ? blob : null;
    }

    public static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
}

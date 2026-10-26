using AIThread.Common.Enums;

namespace AIThread.Common.Entities;

// A file as it was before a thread first wrote to it, so the thread's changes can be undone. One row per thread and
// file: later writes of the same thread (retries, fix rounds) keep the first snapshot and only move AfterHash.
public class ThreadFileSnapshot : BaseEntity
{
    public Guid ThreadRunId { get; set; }
    public ThreadRun? ThreadRun { get; set; }

    // Relative to the project root, forward slashes.
    public string FilePath { get; set; } = string.Empty;

    // False when the thread created the file; undoing then deletes it.
    public bool Existed { get; set; }

    // Exact bytes before the first write (null when the file did not exist).
    public byte[]? OriginalContent { get; set; }

    // SHA-256 (hex) of OriginalContent, and of the bytes the thread last wrote. A file whose current hash is neither
    // was changed by someone else afterwards, and undoing it would lose that work.
    public string? OriginalHash { get; set; }
    public string? AfterHash { get; set; }

    // True when the thread last left the file absent (a delete task, or the source of a move); undoing restores it.
    public bool Removed { get; set; }

    // Git's blob id of what the thread last wrote (git hash-object, with the repository's filters), taken when the
    // thread finished: a commit, a squash or a cherry-pick holding the same content holds the same blob.
    public string? AfterBlob { get; set; }

    // Where this file's change is now (GitChangeTracker) and the commit that brought it in, when committed.
    public GitChangeState? GitState { get; set; }
    public string? GitCommit { get; set; }
}

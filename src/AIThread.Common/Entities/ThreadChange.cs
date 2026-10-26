using AIThread.Common.Enums;

namespace AIThread.Common.Entities;

// The git state of one AIT thread's change, kept next to long memory so every developer sharing the store (and the
// memory written from the thread) knows where that change is: still in the author's working tree, committed, pushed,
// thrown away. Id = the ThreadRun id. Written by the AIT that ran the thread (GitChangeTracker); read by the others to
// tell whether a memory's change is in the branch they have checked out.
public class ThreadChange : BaseEntity
{
    public const int MaxTitleLength = 200;

    public string ProjectKey { get; set; } = string.Empty;
    public string? ProjectName { get; set; }

    // The start of the thread's request, for lists.
    public string? Title { get; set; }

    // Where the thread started (ThreadRun.GitAuthor/GitBranch/GitBaseCommit).
    public string? GitAuthor { get; set; }
    public string? GitBranch { get; set; }
    public string? GitBaseCommit { get; set; }

    // State in the author's clone, and the commits holding the change (comma-separated).
    public GitChangeState? State { get; set; }
    public string? Commits { get; set; }

    // JSON [{"path": "src/A.cs", "blob": "<git blob id>", "removed": false}]: what the thread left in each file, so
    // another clone can look for the same content in its own branches (a squash merge keeps the blob, not the commit).
    public string? Files { get; set; }

    // The git identity of the AIT that last wrote the row.
    public string? UpdatedBy { get; set; }
}

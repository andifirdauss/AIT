namespace AIThread.Common.Enums;

// Where a memory's change is, seen from one clone (GitChangeTracker.PresenceAsync).
public enum MemoryGitPlace
{
    // In the branch checked out here (or nothing tells otherwise: knowledge, older memory, no repository).
    Here = 1,

    // Committed on a branch that is not merged into the one checked out here.
    OtherBranch = 2,

    // Not committed yet (or stashed) in its author's clone.
    InProgress = 3,

    // Thrown away or lost: the planner does not read it.
    Gone = 4
}

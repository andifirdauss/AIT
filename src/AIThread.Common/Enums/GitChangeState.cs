namespace AIThread.Common.Enums;

// Where the change a thread made to the code is now, as git sees it (GitChangeTracker). Ordered by progress: a thread
// whose files are in different states takes the least advanced one of the files it still has (see GitChangeTracker).
public enum GitChangeState
{
    // In the working tree only: neither staged nor committed.
    Uncommitted = 1,

    // Staged (git add), not committed yet.
    Staged = 2,

    // In a commit of the branch checked out now.
    Committed = 3,

    // Changed again outside AIT before being committed: the content is neither what the thread wrote nor what it found.
    ModifiedOutside = 4,

    // Thrown away outside AIT (checkout, discard, reset): the file is back to what the thread found, and no branch or
    // tag holds the change.
    Discarded = 5,

    // In a commit of another branch (local or remote) or a tag, not of the one checked out now: after a checkout, or a
    // change committed on a feature branch that is not merged here.
    OnOtherBranch = 6
}

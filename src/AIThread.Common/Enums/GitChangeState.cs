namespace AIThread.Common.Enums;

// Where the change a thread made to the code is now, as git sees it (GitChangeTracker). A thread whose files are in
// different states takes one state for all of them (GitChangeTracker.Summarize). Stored by name.
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
    OnOtherBranch = 6,

    // Committed and in a remote-tracking branch (pushed, or fetched from someone who pushed it).
    Pushed = 7,

    // Taken off the working tree by git stash: a stash entry holds the change.
    Stashed = 8,

    // It was committed, but no branch or tag holds that commit any more (reset, rebase or force-push dropped it).
    Lost = 9
}

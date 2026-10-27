using AIThread.Common.Enums;

namespace AIThread.Service.Git;

public sealed partial class GitChangeTracker
{
    // Which history the commit of a file's change is looked up in: HEAD's, or every branch and tag (not the stash).
    private enum HistoryScope
    {
        None,
        Head,
        Branches
    }

    // The state of one file of a thread from what is on disk (current: SHA-256, null = absent) and in git, and where
    // the commit that brought the change is looked up: Committed wants its commit; ModifiedOutside may have been
    // committed before it was changed again (then Committed); Discarded may be committed on another branch (then
    // OnOtherBranch). Null when git cannot tell (no fingerprint, ignored file).
    private static (GitChangeState? State, HistoryScope History) StateOf(TrackedFile file, string? current, PathFacts facts)
    {
        if (facts.Ignored) return (null, HistoryScope.None);

        if (file.Removed)
        {
            // Created and removed again by the same thread: nothing for git to hold.
            if (!file.Existed) return (null, HistoryScope.None);
            if (current is not null)
                return current == file.OriginalHash ? (GitChangeState.Discarded, HistoryScope.None) : (GitChangeState.ModifiedOutside, HistoryScope.None);
            if (facts.IndexBlob is not null) return (GitChangeState.Uncommitted, HistoryScope.None);
            if (facts.HeadBlob is not null) return (GitChangeState.Staged, HistoryScope.None);
            return (GitChangeState.Committed, HistoryScope.Head);
        }

        if (file.AfterBlob is null) return (null, HistoryScope.None);
        if (facts.HeadBlob == file.AfterBlob) return (GitChangeState.Committed, HistoryScope.Head);
        if (current == file.AfterHash)
            return (facts.IndexBlob == file.AfterBlob ? GitChangeState.Staged : GitChangeState.Uncommitted, HistoryScope.None);
        // Back to what the thread found (for a created file: gone again) while HEAD does not hold the change.
        if (current == file.OriginalHash) return (GitChangeState.Discarded, HistoryScope.Branches);
        return (GitChangeState.ModifiedOutside, HistoryScope.Head);
    }
}

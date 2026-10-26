using AIThread.Common.Enums;

namespace AIThread.Service.Git;

public sealed partial class GitChangeTracker
{
    // How far a change has come where it is: in the working tree, the index, a commit, a remote.
    private static int Progress(GitChangeState? state) => state switch
    {
        GitChangeState.Uncommitted => 0,
        GitChangeState.Staged => 1,
        GitChangeState.Committed => 2,
        GitChangeState.Pushed => 3,
        _ => -1
    };

    // The state of a thread from its files:
    // - every file gone: Lost when any commit was lost, else Discarded;
    // - leaving the gone ones out, ModifiedOutside when any was changed again;
    // - none left in the working tree or HEAD: Stashed when any is in a stash, else OnOtherBranch;
    // - else the least advanced of those here (e.g. one file committed, one not: Uncommitted).
    // Commits: those holding the files that are not gone. Null when git could tell nothing about any file.
    public static (GitChangeState? State, IReadOnlyList<string> Commits) Summarize(IEnumerable<(GitChangeState? State, string? Commit)> files)
    {
        var known = files.Where(f => f.State is not null).ToList();
        if (known.Count == 0) return (null, []);
        if (known.All(f => f.State is GitChangeState.Discarded or GitChangeState.Lost))
            return (known.Any(f => f.State == GitChangeState.Lost) ? GitChangeState.Lost : GitChangeState.Discarded, []);

        var live = known.Where(f => f.State is not (GitChangeState.Discarded or GitChangeState.Lost)).ToList();
        var commits = live.Where(f => f.Commit is not null).Select(f => f.Commit!).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (live.Any(f => f.State == GitChangeState.ModifiedOutside)) return (GitChangeState.ModifiedOutside, commits);

        var here = live.Where(f => Progress(f.State) >= 0).ToList();
        if (here.Count == 0)
            return (live.Any(f => f.State == GitChangeState.Stashed) ? GitChangeState.Stashed : GitChangeState.OnOtherBranch, commits);
        return (here.MinBy(f => Progress(f.State)).State, commits);
    }
}

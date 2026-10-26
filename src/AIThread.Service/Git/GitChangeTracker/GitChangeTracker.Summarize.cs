using AIThread.Common.Enums;

namespace AIThread.Service.Git;

public sealed partial class GitChangeTracker
{
    // The state of a thread from its files: Discarded when every file was thrown away; otherwise, leaving the thrown
    // away ones out, ModifiedOutside when any was changed again, OnOtherBranch when none is in the working tree or HEAD
    // any more, else the least advanced in the working tree (Uncommitted < Staged < Committed). Commits: those holding
    // its committed files, here or on the other branch. Null when git could tell nothing about any file.
    public static (GitChangeState? State, IReadOnlyList<string> Commits) Summarize(IEnumerable<(GitChangeState? State, string? Commit)> files)
    {
        var known = files.Where(f => f.State is not null).ToList();
        if (known.Count == 0) return (null, []);
        if (known.All(f => f.State == GitChangeState.Discarded)) return (GitChangeState.Discarded, []);

        var live = known.Where(f => f.State != GitChangeState.Discarded).ToList();
        var commits = live.Where(f => f.Commit is not null).Select(f => f.Commit!).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (live.Any(f => f.State == GitChangeState.ModifiedOutside)) return (GitChangeState.ModifiedOutside, commits);
        if (live.All(f => f.State == GitChangeState.OnOtherBranch)) return (GitChangeState.OnOtherBranch, commits);
        return (live.Where(f => f.State != GitChangeState.OnOtherBranch).Min(f => f.State), commits);
    }
}

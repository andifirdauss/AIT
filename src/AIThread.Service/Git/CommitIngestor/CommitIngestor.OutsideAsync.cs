using AIThread.Data.Memory;
using AIThread.Service.Workspace;

namespace AIThread.Service.Git;

public sealed partial class CommitIngestor
{
    // The commits of the last SuggestDays days (the ref's, and the working branch's when read) that AIT does not know:
    // neither an AIT thread's nor taken on before. Cheap (git rev-list and the shared rows, no commit is read), at most
    // MaxSuggested; a commit that only holds what an AIT thread wrote without being tracked is counted too.
    private static async Task<int?> OutsideAsync(IGitTrackingDb db, string key, string root, string reference, bool workingBranch,
        CancellationToken cancellationToken)
    {
        var since = "--since=" + DateTime.UtcNow.AddDays(-SuggestDays).ToString("yyyy-MM-ddTHH:mm:ssZ");
        var list = await GitProcess.RunAsync(root, ["rev-list", "--no-merges", $"--max-count={MaxSuggested}", since, reference], cancellationToken);
        if (!list.Ok) return null;
        var commits = GitProcess.Lines(list.Output).ToList();
        if (workingBranch && await WorkingBranchAsync(root, reference, since, cancellationToken) is { } branch) commits.AddRange(branch.Commits);
        if (commits.Count == 0) return 0;

        var (known, _) = await KnownAsync(db, key, cancellationToken);
        return Math.Min(MaxSuggested, commits.Distinct(StringComparer.OrdinalIgnoreCase).Count(c => !known.Contains(c)));
    }
}

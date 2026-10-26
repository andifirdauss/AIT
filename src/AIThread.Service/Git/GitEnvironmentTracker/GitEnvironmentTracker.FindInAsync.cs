using AIThread.Service.Workspace;

namespace AIThread.Service.Git;

public sealed partial class GitEnvironmentTracker
{
    // The changes an environment's commit (head) holds, with the commit of the environment that brought each:
    // 1. one of the change's commits is in the environment's history (merged, fast-forwarded);
    // 2. the environment's tree holds every blob the change wrote, and lacks the files it removed (squash, cherry-pick);
    // 3. the environment's history once held every such blob (changed again later), for a bounded number of changes.
    private static async Task<IReadOnlyList<(Guid Change, string? Commit)>> FindInAsync(string root, string head, IReadOnlyList<Candidate> candidates,
        CancellationToken cancellationToken)
    {
        var found = new List<(Guid, string?)>();
        var oldest = candidates.Min(c => c.CreatedAt).AddDays(-2).ToString("yyyy-MM-ddTHH:mm:ssZ");
        var history = await GitProcess.RunAsync(root, ["rev-list", "--since=" + oldest, head], cancellationToken);
        var commits = history.Ok ? GitProcess.Lines(history.Output).ToHashSet(StringComparer.OrdinalIgnoreCase) : [];

        var rest = new List<Candidate>();
        foreach (var candidate in candidates)
        {
            if (candidate.Commits.FirstOrDefault(commits.Contains) is { } commit) found.Add((candidate.Id, commit));
            else if (candidate.Files.Count > 0 && candidate.Files.All(f => f.Removed || f.Blob is not null)) rest.Add(candidate);
        }
        if (rest.Count == 0) return found;

        var tree = await TreeBlobsAsync(root, head, rest.SelectMany(c => c.Files).Select(f => f.Path).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            cancellationToken);
        var lookups = 0;
        foreach (var candidate in rest)
        {
            var inTree = candidate.Files.All(f => f.Removed ? !tree.ContainsKey(f.Path) : tree.GetValueOrDefault(f.Path) == f.Blob);
            if (!inTree && lookups++ >= MaxHistoryLookups) continue;

            // In the tree: found, the commit is only looked up (first file). Not in the tree: every file's blob must be in
            // the history.
            string? commit = null;
            var all = true;
            foreach (var file in candidate.Files.Where(f => !f.Removed).Take(inTree ? 1 : int.MaxValue))
            {
                var since = "--since=" + candidate.CreatedAt.AddDays(-2).ToString("yyyy-MM-ddTHH:mm:ssZ");
                var log = await GitProcess.RunAsync(root, ["--literal-pathspecs", "log", "--format=%H", "--find-object=" + file.Blob, since, head, "--", file.Path],
                    cancellationToken);
                var lines = log.Ok ? GitProcess.Lines(log.Output) : [];
                if (lines.Count == 0)
                {
                    all = false;
                    break;
                }
                // The oldest one brought it into the environment.
                commit ??= lines[^1];
            }
            if (inTree || all) found.Add((candidate.Id, commit));
        }
        return found;
    }
}

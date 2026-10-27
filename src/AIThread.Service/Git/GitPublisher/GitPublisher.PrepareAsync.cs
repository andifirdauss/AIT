using AIThread.Service.Workspace;

namespace AIThread.Service.Git;

public sealed partial class GitPublisher
{
    public Task<(PublishJob? Job, string? Error)> PrepareAsync(IReadOnlyList<string> files, bool commit, bool push, string? message,
        CancellationToken cancellationToken = default) =>
        PrepareAsync(files, new PublishRequest(commit, push, message), cancellationToken);

    // Groups the files by the repository holding them, with the branch and its upstream (a new branch has none yet) and
    // the base of a pull request. Files outside any repository are left out; Error says why nothing can be done (no
    // repository, detached HEAD, no remote to push to, the new branch already exists, a pull request into itself).
    public async Task<(PublishJob? Job, string? Error)> PrepareAsync(IReadOnlyList<string> files, PublishRequest request,
        CancellationToken cancellationToken = default)
    {
        var (commit, push) = (request.Commit, request.Push);
        if (request.PullRequest is not null && !push) return (null, "a pull request needs a push");

        var byRoot = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var roots = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var directory = Path.GetDirectoryName(file);
            while (directory is not null && !Directory.Exists(directory)) directory = Path.GetDirectoryName(directory);
            if (directory is null) continue;
            if (!roots.TryGetValue(directory, out var root))
            {
                var top = await GitProcess.RunAsync(directory, ["rev-parse", "--show-toplevel"], cancellationToken);
                roots[directory] = root = top.Ok && top.Output.Trim() is { Length: > 0 } path ? Path.GetFullPath(path) : null;
            }
            if (root is null) continue;
            if (!byRoot.TryGetValue(root, out var list)) byRoot[root] = list = [];
            list.Add(Path.GetRelativePath(root, file).Replace('\\', '/'));
        }
        if (byRoot.Count == 0) return (null, "none of the files is in a git repository");

        var repos = new List<PublishRepo>();
        foreach (var (root, paths) in byRoot)
        {
            var branch = (await GitProcess.RunAsync(root, ["rev-parse", "--abbrev-ref", "HEAD"], cancellationToken)).Output.Trim();
            if (branch.Length == 0 || branch == "HEAD") return (null, $"{root} is not on a branch (detached HEAD)");
            if (request.Branch is { } wanted && await BranchExistsAsync(root, wanted, cancellationToken))
                return (null, $"the branch '{wanted}' already exists in {root}; AIT only creates new branches (switch to it yourself, or name another one)");

            // Only the files git sees as changed are committed; a file the thread left as it found it has nothing to commit.
            var changed = commit ? await ChangedAsync(root, paths, cancellationToken) : [];

            string? remote = null;
            var hasUpstream = false;
            if (push)
            {
                var upstream = request.Branch is null
                    ? await GitProcess.RunAsync(root, ["rev-parse", "--abbrev-ref", "--symbolic-full-name", "@{u}"], cancellationToken)
                    : null;
                if (upstream is { Ok: true } && upstream.Output.Trim() is { Length: > 0 } tracking && tracking.IndexOf('/') is var slash and > 0)
                {
                    remote = tracking[..slash];
                    hasUpstream = true;
                }
                else
                {
                    var remotes = (await GitProcess.RunAsync(root, ["remote"], cancellationToken)).Output
                        .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    remote = remotes.Contains("origin") ? "origin" : remotes.FirstOrDefault();
                    if (remote is null) return (null, $"{root} has no remote to push to");
                }
            }

            string? baseBranch = null;
            if (request.PullRequest is { } pullRequest && remote is not null)
            {
                baseBranch = pullRequest.Base ?? await DefaultBaseAsync(root, remote, cancellationToken);
                if (string.Equals(baseBranch, request.Branch ?? branch, StringComparison.Ordinal))
                    return (null, $"a pull request from {baseBranch} into itself; create a new branch for it or name another base");
            }
            repos.Add(new PublishRepo(root, branch, remote, hasUpstream, changed, request.Branch, baseBranch));
        }

        if (commit && repos.All(r => r.Files.Count == 0) && !push && request.Branch is null) return (null, "the files have no uncommitted change");
        return (new PublishJob(commit, push, request.Message, repos, request.Branch, request.PullRequest), null);
    }
}

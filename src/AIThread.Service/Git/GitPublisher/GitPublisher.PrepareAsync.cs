using AIThread.Service.Workspace;

namespace AIThread.Service.Git;

public sealed partial class GitPublisher
{
    // Groups the files by the repository holding them, with the branch and its upstream. Files outside any repository
    // are left out; Error says why nothing can be done (no repository, detached HEAD, no remote to push to).
    public async Task<(PublishJob? Job, string? Error)> PrepareAsync(IReadOnlyList<string> files, bool commit, bool push, string? message,
        CancellationToken cancellationToken = default)
    {
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

            // Only the files git sees as changed are committed; a file the thread left as it found it has nothing to commit.
            var changed = commit ? await ChangedAsync(root, paths, cancellationToken) : [];

            string? remote = null;
            var hasUpstream = false;
            if (push)
            {
                var upstream = await GitProcess.RunAsync(root, ["rev-parse", "--abbrev-ref", "--symbolic-full-name", "@{u}"], cancellationToken);
                if (upstream.Ok && upstream.Output.Trim() is { Length: > 0 } tracking && tracking.IndexOf('/') is var slash and > 0)
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
            repos.Add(new PublishRepo(root, branch, remote, hasUpstream, changed));
        }

        if (commit && repos.All(r => r.Files.Count == 0) && !push) return (null, "the files have no uncommitted change");
        return (new PublishJob(commit, push, message, repos), null);
    }
}

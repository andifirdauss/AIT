using AIThread.Service.Workspace;

namespace AIThread.Service.Git;

public sealed partial class GitChangeTracker
{
    // Whether any branch, remote-tracking branch or tag still holds the commit. A commit git no longer knows is not.
    private static async Task<bool> ReachableAsync(string root, string commit, RefreshCache cache, CancellationToken cancellationToken)
    {
        if (cache.Reachable.TryGetValue(commit, out var known)) return known;
        var refs = await GitProcess.RunAsync(root,
            ["for-each-ref", "--count=1", "--format=%(refname)", "--contains", commit, "refs/heads", "refs/remotes", "refs/tags"], cancellationToken);
        return cache.Reachable[commit] = refs.Ok && refs.Output.Trim().Length > 0;
    }
}

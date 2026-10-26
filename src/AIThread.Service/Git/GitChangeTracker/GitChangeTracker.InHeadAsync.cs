using AIThread.Service.Workspace;

namespace AIThread.Service.Git;

public sealed partial class GitChangeTracker
{
    // Whether the commit is in HEAD's history (merge-base --is-ancestor answers with its exit code).
    private static async Task<bool> InHeadAsync(string root, string commit, RefreshCache cache, CancellationToken cancellationToken)
    {
        if (cache.InHead.TryGetValue(commit, out var known)) return known;
        var result = await GitProcess.RunAsync(root, ["merge-base", "--is-ancestor", commit, "HEAD"], cancellationToken);
        return cache.InHead[commit] = result.Ok;
    }
}

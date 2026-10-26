using AIThread.Common.Enums;
using AIThread.Service.Workspace;

namespace AIThread.Service.Git;

public sealed partial class GitChangeTracker
{
    // Committed becomes Pushed once a remote-tracking branch holds the commit (as of the last fetch).
    private static async Task<FileResult> PushedAsync(string root, FileResult result, RefreshCache cache, CancellationToken cancellationToken)
    {
        if (result.State != GitChangeState.Committed || result.Commit is not { } commit) return result;
        if (!cache.Pushed.TryGetValue(commit, out var pushed))
        {
            var refs = await GitProcess.RunAsync(root, ["for-each-ref", "--count=1", "--format=%(refname)", "--contains", commit, "refs/remotes"], cancellationToken);
            pushed = cache.Pushed[commit] = refs.Ok && refs.Output.Trim().Length > 0;
        }
        return pushed ? result with { State = GitChangeState.Pushed } : result;
    }
}

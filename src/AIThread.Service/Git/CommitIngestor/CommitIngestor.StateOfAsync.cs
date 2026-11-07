using AIThread.Common.Enums;
using AIThread.Service.Workspace;

namespace AIThread.Service.Git;

public sealed partial class CommitIngestor
{
    // Where the commits of an ingested change are in this clone: Pushed when a remote-tracking branch holds one, Committed
    // when a local branch or tag does, Lost when git still has one of them but no ref holds it any more (the branch was
    // reset, rebased without a copy taken on yet, or deleted). Null when the clone has none of them (another clone's).
    private static async Task<GitChangeState?> StateOfAsync(string root, IReadOnlyList<string> commits, CancellationToken cancellationToken)
    {
        var local = false;
        var known = false;
        foreach (var commit in commits)
        {
            var exists = await GitProcess.RunAsync(root, ["cat-file", "-e", commit + "^{commit}"], cancellationToken);
            if (!exists.Ok) continue;
            known = true;
            if (await ContainsAsync(root, commit, "refs/remotes", cancellationToken)) return GitChangeState.Pushed;
            local |= await ContainsAsync(root, commit, "refs/heads", cancellationToken) || await ContainsAsync(root, commit, "refs/tags", cancellationToken);
        }
        return local ? GitChangeState.Committed : known ? GitChangeState.Lost : null;
    }

    private static async Task<bool> ContainsAsync(string root, string commit, string refs, CancellationToken cancellationToken)
    {
        var result = await GitProcess.RunAsync(root, ["for-each-ref", "--count=1", "--format=%(refname)", "--contains", commit, refs], cancellationToken);
        return result.Ok && result.Output.Trim().Length > 0;
    }
}

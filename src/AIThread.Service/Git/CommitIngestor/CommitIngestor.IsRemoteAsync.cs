using AIThread.Service.Workspace;

namespace AIThread.Service.Git;

public sealed partial class CommitIngestor
{
    // Whether the ref is a remote-tracking branch (its commits are pushed) rather than a local one. The ref is checked when
    // it is saved (never an option); --end-of-options is not used here, rev-parse would print it.
    private static async Task<bool> IsRemoteAsync(string root, string reference, CancellationToken cancellationToken)
    {
        var result = await GitProcess.RunAsync(root, ["rev-parse", "--symbolic-full-name", reference], cancellationToken);
        return result.Ok && result.Output.Trim().StartsWith("refs/remotes/", StringComparison.Ordinal);
    }
}

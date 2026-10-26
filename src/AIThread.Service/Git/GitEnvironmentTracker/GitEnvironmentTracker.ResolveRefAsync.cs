using AIThread.Service.Workspace;

namespace AIThread.Service.Git;

public sealed partial class GitEnvironmentTracker
{
    // The commit an environment's ref points to in this clone; null when the clone does not know it (not fetched, a typo).
    private static async Task<string?> ResolveRefAsync(string root, string reference, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(root) || !GitTools.IsRepository(root)) return null;
        var result = await GitProcess.RunAsync(root, ["rev-parse", "--verify", "--quiet", "--end-of-options", reference + "^{commit}"], cancellationToken);
        return result.Ok && result.Output.Trim() is { Length: > 0 } commit ? commit : null;
    }
}

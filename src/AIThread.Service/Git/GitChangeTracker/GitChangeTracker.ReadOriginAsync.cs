using AIThread.Service.Workspace;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.Git;

public sealed partial class GitChangeTracker
{
    // Null outside a repository or when git cannot run; never throws, a thread starts whatever git says.
    public async Task<GitOrigin?> ReadOriginAsync(string root, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root) || !GitTools.IsRepository(root)) return null;
        try
        {
            var branch = GitProcess.RunAsync(root, ["rev-parse", "--abbrev-ref", "HEAD"], cancellationToken, timeout: OriginTimeout);
            var commit = GitProcess.RunAsync(root, ["rev-parse", "--verify", "--quiet", "HEAD"], cancellationToken, timeout: OriginTimeout);
            // The repository's own config wins over the global one, as for a commit.
            var name = GitProcess.RunAsync(root, ["config", "user.name"], cancellationToken, timeout: OriginTimeout);
            var email = GitProcess.RunAsync(root, ["config", "user.email"], cancellationToken, timeout: OriginTimeout);
            await Task.WhenAll(branch, commit, name, email);

            var author = Author(Value(await name), Value(await email));
            var origin = new GitOrigin(Clip(Value(await branch), 250), Clip(Value(await commit), 64), Clip(author, 300));
            return origin is { Branch: null, Commit: null, Author: null } ? null : origin;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "Reading git of {Root} failed", root);
            return null;
        }
    }
}

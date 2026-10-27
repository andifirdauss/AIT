using AIThread.Service.Workspace;

namespace AIThread.Service.Git;

public sealed partial class GitPublisher
{
    // Whether the repository already has a local branch of that name.
    private static async Task<bool> BranchExistsAsync(string root, string branch, CancellationToken cancellationToken) =>
        (await GitProcess.RunAsync(root, ["rev-parse", "--verify", "--quiet", $"refs/heads/{branch}"], cancellationToken)).Ok;
}

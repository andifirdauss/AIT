using AIThread.Service.Workspace;

namespace AIThread.Service.Git;

public sealed partial class GitPublisher
{
    // Creates the branch from HEAD and switches to it; the uncommitted changes move along. An existing branch is never
    // reset or reused (it may hold someone's work).
    private static async Task<GitResult> SwitchBranchAsync(string root, string branch, CancellationToken cancellationToken)
    {
        if (await BranchExistsAsync(root, branch, cancellationToken))
            return new GitResult(string.Empty, $"a branch named '{branch}' already exists; AIT does not reset or reuse it.");
        return await GitProcess.RunAsync(root, ["switch", "-c", branch], cancellationToken, timeout: WriteTimeout);
    }
}

namespace AIThread.Service.Git;

public sealed partial class GitPublisher
{
    // Branches whose push is never allowed "always": the shared mainline, production and release branches.
    public static bool IsProtectedBranch(string branch) =>
        branch is "main" or "master" or "production" or "prod" || branch.StartsWith("release/", StringComparison.OrdinalIgnoreCase);
}

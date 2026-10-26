using AIThread.Service.Workspace;

namespace AIThread.Service.Git;

public sealed partial class GitChangeTracker
{
    private static string? Value(GitResult result) => result.Ok && result.Output.Trim() is { Length: > 0 } value ? value : null;
}

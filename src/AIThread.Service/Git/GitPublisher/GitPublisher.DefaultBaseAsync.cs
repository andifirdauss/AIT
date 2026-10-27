using AIThread.Service.Workspace;

namespace AIThread.Service.Git;

public sealed partial class GitPublisher
{
    // The remote's default branch (refs/remotes/<remote>/HEAD), else its main or master, else "main".
    private static async Task<string> DefaultBaseAsync(string root, string remote, CancellationToken cancellationToken)
    {
        var head = await GitProcess.RunAsync(root, ["symbolic-ref", "--quiet", "--short", $"refs/remotes/{remote}/HEAD"], cancellationToken);
        var prefix = remote + "/";
        if (head.Ok && head.Output.Trim() is { } name && name.StartsWith(prefix, StringComparison.Ordinal) && name.Length > prefix.Length)
            return name[prefix.Length..];

        foreach (var candidate in new[] { "main", "master" })
            if ((await GitProcess.RunAsync(root, ["rev-parse", "--verify", "--quiet", $"refs/remotes/{remote}/{candidate}"], cancellationToken)).Ok)
                return candidate;
        return "main";
    }
}

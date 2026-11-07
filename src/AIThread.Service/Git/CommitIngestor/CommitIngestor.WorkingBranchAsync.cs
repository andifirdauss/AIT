using AIThread.Service.Workspace;

namespace AIThread.Service.Git;

public sealed partial class CommitIngestor
{
    // The branch checked out in the clone and its commits since the moment that the ingest ref does not hold, oldest first
    // (a feature branch, or a local main ahead of origin/main). Null when HEAD is detached (a rebase or bisect in progress
    // moves it commit by commit) or git cannot tell. since = the --since argument of the pass.
    private static async Task<WorkingBranch?> WorkingBranchAsync(string root, string reference, string? since, CancellationToken cancellationToken)
    {
        var head = await GitProcess.RunAsync(root, ["symbolic-ref", "--quiet", "--short", "HEAD"], cancellationToken);
        var name = head.Ok ? head.Output.Trim() : string.Empty;
        if (name.Length == 0) return null;

        // The ref is checked when it is saved (never an option), and "^ref" excludes its history.
        List<string> arguments = ["rev-list", "--no-merges", "--reverse"];
        if (since is not null) arguments.Add(since);
        arguments.AddRange(["HEAD", "^" + reference]);
        var list = await GitProcess.RunAsync(root, arguments, cancellationToken);
        return list.Ok ? new WorkingBranch(name, GitProcess.Lines(list.Output)) : null;
    }
}

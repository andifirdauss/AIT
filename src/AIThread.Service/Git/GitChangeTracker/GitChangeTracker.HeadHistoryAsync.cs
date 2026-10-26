using AIThread.Service.Workspace;

namespace AIThread.Service.Git;

public sealed partial class GitChangeTracker
{
    // The commits of HEAD's history since a moment (with two days of slack for clocks and for threads that ran long), in
    // one git call: whether a shared change is in this branch is then a lookup, however many memories ask.
    private static async Task<HashSet<string>> HeadHistoryAsync(string root, DateTime since, CancellationToken cancellationToken)
    {
        var after = "--since=" + since.AddDays(-2).ToString("yyyy-MM-ddTHH:mm:ssZ");
        var result = await GitProcess.RunAsync(root, ["rev-list", after, "HEAD"], cancellationToken);
        return result.Ok ? GitProcess.Lines(result.Output).ToHashSet(StringComparer.OrdinalIgnoreCase) : [];
    }
}

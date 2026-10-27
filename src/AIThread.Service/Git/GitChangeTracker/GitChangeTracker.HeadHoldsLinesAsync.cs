using AIThread.Service.Workspace;

namespace AIThread.Service.Git;

public sealed partial class GitChangeTracker
{
    // Whether the file in HEAD holds most of the lines the thread added and lacks most of those it removed: the
    // thread's change, committed with the developer's own edits on top (a later fix, a reformatted line).
    private static async Task<bool> HeadHoldsLinesAsync(string root, string path, ThreadLines lines, RefreshCache cache, CancellationToken cancellationToken)
    {
        if (lines.Added.Count == 0) return false;
        if (!cache.HeadLines.TryGetValue(path, out var head))
        {
            // "HEAD:./path" is relative to root, where git runs.
            var result = await GitProcess.RunAsync(root, ["show", "HEAD:./" + path], cancellationToken);
            head = cache.HeadLines[path] = result.Ok ? Normalize(result.Output) : null;
        }
        if (head is null) return false;

        var present = head.ToHashSet(StringComparer.Ordinal);
        var added = lines.Added.Count(present.Contains) / (double)lines.Added.Count;
        var removed = lines.Removed.Where(l => !lines.Added.Contains(l)).ToList();
        var gone = removed.Count == 0 ? 1 : removed.Count(l => !present.Contains(l)) / (double)removed.Count;
        return added >= LineMatchShare && gone >= LineMatchShare;
    }
}

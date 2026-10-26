using System.Globalization;
using AIThread.Service.Workspace;

namespace AIThread.Service.Git;

public sealed partial class CommitIngestor
{
    // Author, time, message and the files of one commit with the blob each holds after it (paths relative to root, only
    // those below it: --relative). Null when git cannot read it.
    private static async Task<CommitInfo?> ReadCommitAsync(string root, string sha, CancellationToken cancellationToken)
    {
        var head = await GitProcess.RunAsync(root, ["show", "-s", "--format=%an <%ae>%x00%cI%x00%B", sha], cancellationToken);
        if (!head.Ok) return null;
        var parts = head.Output.Split('\0', 3);
        if (parts.Length < 3) return null;
        var committedAt = DateTimeOffset.TryParse(parts[1].Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var at) ? at.UtcDateTime : DateTime.UtcNow;

        // ":<old mode> <new mode> <old blob> <new blob> <status>" then the path (two for a rename or copy), NUL-separated.
        var tree = await GitProcess.RunAsync(root, ["diff-tree", "--no-commit-id", "-r", "-z", "--root", "--relative", "--no-renames", sha], cancellationToken);
        if (!tree.Ok) return null;
        var entries = GitProcess.Entries(tree.Output);
        var files = new List<SharedFile>();
        for (var i = 0; i + 1 < entries.Count; i += 2)
        {
            if (!entries[i].StartsWith(':')) break;
            var meta = entries[i][1..].Split(' ');
            if (meta.Length < 5) continue;
            var removed = meta[4].StartsWith('D');
            files.Add(new SharedFile(entries[i + 1], removed ? null : meta[3], removed));
        }
        return new CommitInfo(sha, parts[0].Trim(), committedAt, parts[2].Trim(), files);
    }
}

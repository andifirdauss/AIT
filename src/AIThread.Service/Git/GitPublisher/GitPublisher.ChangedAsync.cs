using AIThread.Service.Workspace;

namespace AIThread.Service.Git;

public sealed partial class GitPublisher
{
    // The paths (relative to the repository) git reports as modified, added, deleted or untracked.
    private static async Task<List<string>> ChangedAsync(string root, IReadOnlyList<string> paths, CancellationToken cancellationToken)
    {
        var status = await GitProcess.RunAsync(root, ["status", "--porcelain=v1", "-z", "--untracked-files=all", "--", .. paths], cancellationToken);
        if (!status.Ok) return [];

        var changed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var entries = status.Output.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < entries.Length; i++)
        {
            var entry = entries[i];
            if (entry.Length < 4) continue;
            changed.Add(entry[3..]);
            // A rename names its source in the next entry.
            if (entry[0] is 'R' or 'C' && i + 1 < entries.Length) changed.Add(entries[++i]);
        }
        return paths.Where(changed.Contains).ToList();
    }
}

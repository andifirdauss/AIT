using AIThread.Service.Workspace;

namespace AIThread.Service.Git;

public sealed partial class GitChangeTracker
{
    // The blobs the refresh's paths have in the newest stash entries: the working tree commit of an entry, and its third
    // parent that holds the untracked files of git stash -u. Read once per refresh, and only when a change looks thrown
    // away.
    private static async Task<Dictionary<string, HashSet<string>>> StashBlobsAsync(string root, RefreshCache cache, CancellationToken cancellationToken)
    {
        if (cache.StashBlobs is { } known) return known;
        var blobs = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        cache.StashBlobs = blobs;

        var list = await GitProcess.RunAsync(root, ["stash", "list", "--format=%H"], cancellationToken);
        if (!list.Ok) return blobs;
        foreach (var stash in GitProcess.Lines(list.Output).Take(MaxStashes))
        {
            foreach (var (tree, chunk) in new[] { stash, stash + "^3" }.SelectMany(t => cache.Paths.Chunk(PathsPerCall).Select(c => (t, c))))
            {
                // Output paths are relative to root, like the snapshots'.
                var result = await GitProcess.RunAsync(root, ["--literal-pathspecs", "ls-tree", "-z", tree, "--", .. chunk], cancellationToken);
                if (!result.Ok) continue;
                foreach (var entry in GitProcess.Entries(result.Output))
                {
                    var tab = entry.IndexOf('\t');
                    if (tab <= 0 || entry[..tab].Split(' ') is not [_, "blob", var blob]) continue;
                    var path = entry[(tab + 1)..];
                    if (!blobs.TryGetValue(path, out var set)) blobs[path] = set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    set.Add(blob);
                }
            }
        }
        return blobs;
    }
}

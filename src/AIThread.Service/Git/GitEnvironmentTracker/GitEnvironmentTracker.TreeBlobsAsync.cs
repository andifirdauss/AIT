using AIThread.Service.Workspace;

namespace AIThread.Service.Git;

public sealed partial class GitEnvironmentTracker
{
    // The blob of each path in a commit's tree (paths relative to root, where git runs); paths it lacks are left out.
    private static async Task<Dictionary<string, string>> TreeBlobsAsync(string root, string commit, IReadOnlyList<string> paths,
        CancellationToken cancellationToken)
    {
        var blobs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var chunk in paths.Chunk(PathsPerCall))
        {
            var result = await GitProcess.RunAsync(root, ["--literal-pathspecs", "ls-tree", "-z", commit, "--", .. chunk], cancellationToken);
            foreach (var entry in GitProcess.Entries(result.Output))
            {
                var tab = entry.IndexOf('\t');
                if (tab > 0 && entry[..tab].Split(' ') is [_, "blob", var blob]) blobs[entry[(tab + 1)..]] = blob;
            }
        }
        return blobs;
    }
}

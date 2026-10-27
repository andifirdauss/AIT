using AIThread.Service.Workspace;

namespace AIThread.Service.Git;

public sealed partial class GitChangeTracker
{
    // The blob of each path in the index and in HEAD, and whether git ignores it. Paths are relative to root (git runs
    // there, so its output is relative to root too) and taken literally, never as patterns.
    private static async Task<Dictionary<string, PathFacts>> ReadFactsAsync(string root, IReadOnlyList<string> paths, CancellationToken cancellationToken)
    {
        var index = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var head = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var ignored = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var chunk in paths.Chunk(PathsPerCall))
        {
            // "<mode> <blob> <stage>\t<path>"
            var staged = await GitProcess.RunAsync(root, ["--literal-pathspecs", "ls-files", "--stage", "-z", "--", .. chunk], cancellationToken);
            foreach (var entry in GitProcess.Entries(staged.Output))
                if (Split(entry) is { } e) index.TryAdd(e.Path, e.Meta.Split(' ')[1]);

            // "<mode> <type> <blob>\t<path>"; fails before the first commit, when HEAD holds nothing.
            var committed = await GitProcess.RunAsync(root, ["--literal-pathspecs", "ls-tree", "-z", "HEAD", "--", .. chunk], cancellationToken);
            foreach (var entry in GitProcess.Entries(committed.Output))
                if (Split(entry) is { } e && e.Meta.Split(' ') is [_, "blob", var blob]) head[e.Path] = blob;

            // Exit code 1 = none ignored. Tracked files are never reported, whatever .gitignore says.
            var check = await GitProcess.RunAsync(root, ["check-ignore", "-z", "--stdin"], cancellationToken, string.Join('\0', chunk) + "\0");
            foreach (var path in GitProcess.Entries(check.Output)) ignored.Add(path);
        }

        return paths.Distinct(StringComparer.OrdinalIgnoreCase).ToDictionary(p => p,
            p => new PathFacts(index.GetValueOrDefault(p), head.GetValueOrDefault(p), ignored.Contains(p)), StringComparer.OrdinalIgnoreCase);

        static (string Meta, string Path)? Split(string entry) =>
            entry.IndexOf('\t') is var tab and > 0 ? (entry[..tab], entry[(tab + 1)..]) : null;
    }
}

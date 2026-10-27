using AIThread.Service.Workspace;

namespace AIThread.Service.Git;

public sealed partial class GitChangeTracker
{
    // The newest commit of HEAD's history since the thread started that touched the file; null when none did.
    private static async Task<string?> LastCommitAsync(string root, string path, DateTime since, Dictionary<string, string?> cache,
        CancellationToken cancellationToken)
    {
        var key = $"last|{path}|{since:O}";
        if (cache.TryGetValue(key, out var known)) return known;
        var after = "--since=" + since.AddDays(-1).ToString("yyyy-MM-ddTHH:mm:ssZ");
        var result = await GitProcess.RunAsync(root, ["--literal-pathspecs", "log", "-n1", "--format=%H", after, "HEAD", "--", path], cancellationToken);
        return cache[key] = result.Ok ? GitProcess.Lines(result.Output).FirstOrDefault() : null;
    }
}

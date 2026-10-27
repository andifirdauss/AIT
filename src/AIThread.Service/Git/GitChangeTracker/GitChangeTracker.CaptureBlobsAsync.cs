using AIThread.Service.Workspace;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.Git;

public sealed partial class GitChangeTracker
{
    // Git's blob id of each file as it is on disk now, with the repository's filters (.gitattributes, autocrlf), as a
    // commit of it would store it. Paths git could not hash are left out.
    private async Task<Dictionary<string, string>> CaptureBlobsAsync(string root, IReadOnlyList<string> paths, CancellationToken cancellationToken)
    {
        var blobs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var chunk in paths.Chunk(PathsPerCall))
        {
            // --stdin-paths reads paths from the top of the repository; absolute ones work wherever the project sits.
            var input = string.Join('\n', chunk.Select(p => Path.GetFullPath(Path.Combine(root, p)))) + "\n";
            var result = await GitProcess.RunAsync(root, ["hash-object", "--stdin-paths"], cancellationToken, input);
            var lines = GitProcess.Lines(result.Output);
            if (!result.Ok || lines.Count != chunk.Length)
            {
                logger.LogDebug("git hash-object failed in {Root}: {Error}", root, result.Error);
                continue;
            }
            for (var i = 0; i < chunk.Length; i++) blobs[chunk[i]] = lines[i].Trim();
        }
        return blobs;
    }
}

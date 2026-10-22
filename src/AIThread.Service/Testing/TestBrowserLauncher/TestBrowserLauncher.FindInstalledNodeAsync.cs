using Microsoft.Extensions.Logging;

namespace AIThread.Service.Testing;

public sealed partial class TestBrowserLauncher
{
    // The first node.exe on PATH that is new enough for the driver; null when there is none.
    private async Task<string?> FindInstalledNodeAsync(CancellationToken cancellationToken)
    {
        var candidates = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(directory => Path.Combine(directory.Trim('"'), "node.exe"))
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var path in candidates)
        {
            var major = await ReadNodeMajorAsync(path, cancellationToken);
            if (major >= MinNodeMajor) return path;
            logger.LogInformation("Node.js at {Path} is not usable for Playwright (major version {Major}, needs {Min}+)",
                path, major?.ToString() ?? "unknown", MinNodeMajor);
        }
        return null;
    }
}

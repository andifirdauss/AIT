namespace AIThread.Service.CliBackend;

public sealed partial class ClaudeCliAdapter
{
    // A configured path: an executable, the package's cli.js (run by node) or the npm shim.
    private static Launch? FromFile(string path)
    {
        if (!File.Exists(path)) return null;
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".js" or ".mjs" or ".cjs" => FindOnPath(OperatingSystem.IsWindows() ? "node.exe" : "node") is { } node ? new Launch(node, [path]) : null,
            ".cmd" or ".bat" => FromNpmShim(path),
            _ => new Launch(path, [])
        };
    }
}

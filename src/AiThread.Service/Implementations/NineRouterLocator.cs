using System.Net;

namespace AiThread.Service.Implementations;

// Where 9router's cli.js came from: a configured path, the global npm folder, or node_modules beside AiThread.exe
// (the aithread npm package depends on 9router).
public enum NineRouterSource { Configured, Global, Bundled }

public sealed record NineRouterCli(string Path, NineRouterSource Source);

// Finds node, npm and 9router on disk. Pure over its inputs so tests can fake the file system.
public static class NineRouterLocator
{
    public const string PackageName = "9router";

    private static string CliUnder(string nodeModules) => Path.Combine(nodeModules, PackageName, "cli.js");

    // Global npm installs put 9router.cmd in the prefix folder (on PATH) and the package in <prefix>\node_modules.
    // Global wins over the bundled copy: users update it with `npm i -g 9router@latest`.
    public static NineRouterCli? FindCli(
        string? configuredPath,
        IEnumerable<string> pathDirs,
        string? appData,
        string baseDirectory,
        Func<string, bool> fileExists)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath) && fileExists(configuredPath))
            return new NineRouterCli(Path.GetFullPath(configuredPath), NineRouterSource.Configured);

        var prefixes = pathDirs.Where(dir => fileExists(Path.Combine(dir, PackageName + ".cmd"))).ToList();
        if (!string.IsNullOrEmpty(appData)) prefixes.Add(Path.Combine(appData, "npm"));
        foreach (var prefix in prefixes)
        {
            var cli = CliUnder(Path.Combine(prefix, "node_modules"));
            if (fileExists(cli)) return new NineRouterCli(cli, NineRouterSource.Global);
        }

        // npm nests a global package's dependencies under it: ...\aithread\node_modules\{aithread-win-x64\bin, 9router}.
        for (var dir = new DirectoryInfo(baseDirectory); dir is not null; dir = dir.Parent)
        {
            var cli = CliUnder(Path.Combine(dir.FullName, "node_modules"));
            if (fileExists(cli)) return new NineRouterCli(cli, NineRouterSource.Bundled);
        }

        return null;
    }

    public static string? FindExecutable(string fileName, string? configuredPath, IEnumerable<string> dirs, Func<string, bool> fileExists)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath) && fileExists(configuredPath)) return Path.GetFullPath(configuredPath);
        return dirs.Select(dir => Path.Combine(dir, fileName)).FirstOrDefault(fileExists);
    }

    // The process PATH can predate a Node.js or npm install made while AiThread runs, so the user and machine PATH
    // from the registry are searched too, plus the Node.js installer's default folder.
    public static IReadOnlyList<string> SearchDirs()
    {
        var entries = new[]
            {
                Environment.GetEnvironmentVariable("PATH"),
                Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User),
                Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine)
            }
            .SelectMany(path => (path ?? "").Split(Path.PathSeparator))
            .Select(dir => Environment.ExpandEnvironmentVariables(dir.Trim().Trim('"')))
            .Append(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "nodejs"));

        var dirs = new List<string>();
        foreach (var dir in entries)
        {
            if (dir.Length == 0 || dir.IndexOfAny(Path.GetInvalidPathChars()) >= 0) continue;
            if (!dirs.Contains(dir, StringComparer.OrdinalIgnoreCase)) dirs.Add(dir);
        }
        return dirs;
    }

    // AiThread only starts 9router on this machine; a remote URL is someone else's server.
    public static bool TryGetLocalPort(string baseUrl, out int port)
    {
        port = 0;
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) return false;
        var local = uri.IsLoopback || (IPAddress.TryParse(uri.Host.Trim('[', ']'), out var address) && IPAddress.IsLoopback(address));
        if (!local) return false;
        port = uri.Port;
        return true;
    }
}

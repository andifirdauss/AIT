using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AiThread.Common.Enums;

namespace AiThread.Service.Workspace;

public sealed record ProjectKeyInfo(string Key, ProjectKeySource Source);

// The same repository is cloned to a different folder on every developer's machine, so memory is keyed by something
// the clones share: an override file committed in the repo, a key typed in the Project menu, or the git remote.
// Files are read directly; git itself is never run.
public static partial class ProjectKeyResolver
{
    public const string OverrideFile = ".aithread/project.json";
    public const int MaxKeyLength = 300;
    public const string LocalPrefix = "local:";

    public static ProjectKeyInfo Resolve(string rootPath, string? manualKey)
    {
        if (FromOverrideFile(rootPath) is { } fileKey) return new ProjectKeyInfo(fileKey, ProjectKeySource.File);
        if (NormalizeKey(manualKey) is { } manual) return new ProjectKeyInfo(manual, ProjectKeySource.Manual);
        if (FromGitRemote(rootPath) is { } remote) return new ProjectKeyInfo(remote, ProjectKeySource.GitRemote);
        return new ProjectKeyInfo(LocalKey(rootPath), ProjectKeySource.Local);
    }

    public static bool IsLocal(string? key) => key?.StartsWith(LocalPrefix, StringComparison.Ordinal) == true;

    // Keys typed by people: case and surrounding spaces do not matter ("EEP" and " eep " are the same project).
    public static string? NormalizeKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;
        var normalized = WhitespacePattern().Replace(key.Trim().ToLowerInvariant(), "-");
        return normalized.Length > MaxKeyLength ? normalized[..MaxKeyLength] : normalized;
    }

    // https://user:token@github.com/Org/Repo.git, git@github.com:org/repo and ssh://git@github.com:22/org/repo all
    // become github.com/org/repo. Local paths are not a shareable identity and return null.
    public static string? NormalizeRemoteUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        var value = url.Trim();

        string host, path;
        var scp = ScpPattern().Match(value);
        if (!value.Contains("://", StringComparison.Ordinal) && scp.Success)
        {
            host = scp.Groups["host"].Value;
            path = scp.Groups["path"].Value;
        }
        else if (Uri.TryCreate(value, UriKind.Absolute, out var uri) && !uri.IsFile && !string.IsNullOrEmpty(uri.Host))
        {
            host = uri.Host;
            path = Uri.UnescapeDataString(uri.AbsolutePath);
        }
        else
        {
            return null;
        }

        path = path.Trim('/');
        if (path.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) path = path[..^4];
        path = path.TrimEnd('/');
        host = host.ToLowerInvariant();
        if (path.Length == 0) return null;

        // Azure DevOps ssh (ssh.dev.azure.com/v3/org/project/repo) and https (dev.azure.com/org/project/_git/repo).
        if (host == "ssh.dev.azure.com" && path.StartsWith("v3/", StringComparison.OrdinalIgnoreCase))
        {
            var parts = path[3..].Split('/');
            if (parts.Length == 3)
            {
                host = "dev.azure.com";
                path = $"{parts[0]}/{parts[1]}/_git/{parts[2]}";
            }
        }

        return NormalizeKey($"{host}/{path}");
    }

    private static string? FromOverrideFile(string rootPath)
    {
        try
        {
            var file = Path.Combine(rootPath, OverrideFile);
            if (!File.Exists(file)) return null;

            using var document = JsonDocument.Parse(File.ReadAllText(file), new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            });
            return document.RootElement.ValueKind == JsonValueKind.Object
                   && document.RootElement.TryGetProperty("key", out var key)
                   && key.ValueKind == JsonValueKind.String
                ? NormalizeKey(key.GetString())
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private static string? FromGitRemote(string rootPath)
    {
        try
        {
            var config = GitConfigPath(rootPath);
            if (config is null || !File.Exists(config)) return null;

            var remotes = ParseRemotes(File.ReadAllLines(config));
            var url = remotes.TryGetValue("origin", out var origin) ? origin : remotes.Values.FirstOrDefault();
            return NormalizeRemoteUrl(url);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    // .git is a folder in a normal clone, and a file ("gitdir: ...") in a worktree or submodule. A worktree keeps its
    // config in the main repository, named by the "commondir" file.
    private static string? GitConfigPath(string rootPath)
    {
        var dotGit = Path.Combine(rootPath, ".git");
        if (Directory.Exists(dotGit)) return Path.Combine(dotGit, "config");
        if (!File.Exists(dotGit)) return null;

        var line = File.ReadLines(dotGit).FirstOrDefault(l => l.StartsWith("gitdir:", StringComparison.OrdinalIgnoreCase));
        if (line is null) return null;

        var gitDir = Path.GetFullPath(Path.Combine(rootPath, line["gitdir:".Length..].Trim()));
        var commonDirFile = Path.Combine(gitDir, "commondir");
        if (File.Exists(commonDirFile))
        {
            var commonDir = File.ReadAllText(commonDirFile).Trim();
            if (commonDir.Length > 0) gitDir = Path.GetFullPath(Path.Combine(gitDir, commonDir));
        }
        return Path.Combine(gitDir, "config");
    }

    private static Dictionary<string, string> ParseRemotes(IEnumerable<string> lines)
    {
        var remotes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? remote = null;
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.StartsWith('['))
            {
                var section = RemoteSectionPattern().Match(line);
                remote = section.Success ? section.Groups["name"].Value : null;
                continue;
            }
            if (remote is null || remotes.ContainsKey(remote)) continue;

            var eq = line.IndexOf('=');
            if (eq <= 0 || !line[..eq].Trim().Equals("url", StringComparison.OrdinalIgnoreCase)) continue;
            remotes[remote] = line[(eq + 1)..].Trim().Trim('"');
        }
        return remotes;
    }

    private static string LocalKey(string rootPath)
    {
        var normalized = Path.TrimEndingDirectorySeparator(rootPath).ToLowerInvariant();
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
        return LocalPrefix + hash[..16];
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespacePattern();

    // scp-like syntax: [user@]host:path, where host is not a single drive letter (C:\repo).
    [GeneratedRegex(@"^(?:[^@/\s]+@)?(?<host>[A-Za-z0-9.\-]{2,}):(?<path>[^\\].*)$")]
    private static partial Regex ScpPattern();

    [GeneratedRegex(@"^\[\s*remote\s+""(?<name>[^""]+)""\s*\]$", RegexOptions.IgnoreCase)]
    private static partial Regex RemoteSectionPattern();
}

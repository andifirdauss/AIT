using System.Text.RegularExpressions;

namespace AIThread.Service.Git;

public sealed partial class GitPublisher
{
    // The web host and repository path of a remote URL (https, ssh:// or scp-like git@host:owner/repo.git), without
    // ".git"; null for a local path or anything else. Azure DevOps ssh (v3/org/project/repo) becomes its web form.
    public static (string Host, string Path)? RemoteWeb(string? remoteUrl)
    {
        var text = (remoteUrl ?? string.Empty).Trim();
        if (text.Length == 0) return null;

        string host, path;
        if (Uri.TryCreate(text, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http" or "ssh" or "git")
        {
            host = uri.Host;
            path = Uri.UnescapeDataString(uri.AbsolutePath);
        }
        else if (ScpLikeRegex().Match(text) is { Success: true } match)
        {
            host = match.Groups["host"].Value;
            path = match.Groups["path"].Value;
        }
        else
        {
            return null;
        }

        path = path.Trim('/');
        if (path.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) path = path[..^4];
        if (host.Length == 0 || path.Length == 0) return null;

        if (host.Equals("ssh.dev.azure.com", StringComparison.OrdinalIgnoreCase) || host.EndsWith("vs-ssh.visualstudio.com", StringComparison.OrdinalIgnoreCase))
        {
            var parts = path.Split('/');
            if (parts is not ["v3", var organization, var project, var repository]) return null;
            return ("dev.azure.com", $"{organization}/{project}/_git/{repository}");
        }
        return (host.ToLowerInvariant(), path);
    }

    // user@host:path, with a host of two characters or more (so "C:\repo" is a path, not a host).
    [GeneratedRegex(@"^(?:[^@/\s]+@)?(?<host>[^:/\s]{2,}):(?!//)(?<path>[^\s]+)$")]
    private static partial Regex ScpLikeRegex();
}

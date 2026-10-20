using System.Text.RegularExpressions;
using AIThread.Common.Enums;
using AIThread.Service.Models;

namespace AIThread.Service.Skills;

// Url is the normalized link stored on the source (also its duplicate key). DownloadUrl is a ZIP archive for
// GitHub/GitLab or the file itself for RawFile; CloneUrl is used with git when there is no archive or it fails.
public sealed record SkillSourceLocation(
    SkillSourceKind Kind,
    string Url,
    string Name,
    string? Ref,
    string? SubPath,
    string? DownloadUrl,
    string? CloneUrl);

// Turns what a user pastes (a repository, a folder or file inside one, a .git URL, a web page) into how to pull it.
public static partial class SkillSourceLocator
{
    public static SkillSourceLocation Locate(string? input)
    {
        var text = (input ?? string.Empty).Trim();
        if (text.Length == 0) throw new AppValidationException("Link skill wajib diisi.");
        if (text.Length > 800) throw new AppValidationException("Link skill maksimal 800 karakter.");

        // "owner/repo" is shorthand for a GitHub repository.
        if (ShorthandRepo().IsMatch(text)) text = $"https://github.com/{text}";

        // scp-style git remote: git@host:owner/repo.git
        if (ScpGit().Match(text) is { Success: true } scp)
            return new SkillSourceLocation(SkillSourceKind.Git, text, Limit(TrimGit(scp.Groups["path"].Value)), null, null, null, text);

        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new AppValidationException("Link skill tidak valid. Contoh: https://github.com/anthropics/skills");

        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Uri.UnescapeDataString).ToArray();
        var host = uri.Host.ToLowerInvariant();
        var normalized = $"{uri.Scheme}://{uri.Authority}{uri.AbsolutePath.TrimEnd('/')}{uri.Query}";

        if (host is "github.com" or "www.github.com" && segments.Length >= 2) return GitHub(segments, normalized);
        if (host == "raw.githubusercontent.com" && segments.Length >= 4 && IsMarkdown(uri.AbsolutePath)) return RawFile(normalized, segments);
        if (host is "gitlab.com" or "www.gitlab.com" && segments.Length >= 2) return GitLab(segments, normalized);

        if (uri.AbsolutePath.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            return new SkillSourceLocation(SkillSourceKind.Git, normalized, Limit(TrimGit(string.Join('/', segments.TakeLast(2)))), null, null, null, normalized);

        if (IsMarkdown(uri.AbsolutePath)) return RawFile(normalized, segments);

        var pageName = segments.Length == 0 ? host : $"{host}/{string.Join('/', segments)}";
        return new SkillSourceLocation(SkillSourceKind.Web, normalized, Limit(pageName), null, null, null, null);
    }

    private static SkillSourceLocation GitHub(string[] segments, string normalized)
    {
        var owner = segments[0];
        var repo = TrimGit(segments[1]);
        string? reference = null;
        string? subPath = null;

        if (segments.Length >= 4 && segments[2] is "tree" or "blob")
        {
            reference = segments[3];
            subPath = segments.Length > 4 ? string.Join('/', segments.Skip(4)) : null;

            // A link to one Markdown file is that file alone.
            if (segments[2] == "blob" && subPath is not null && IsMarkdown(subPath))
                return RawFile(normalized, segments) with { DownloadUrl = $"https://raw.githubusercontent.com/{owner}/{repo}/{reference}/{subPath}" };
        }

        var archive = $"https://api.github.com/repos/{owner}/{repo}/zipball" + (reference is null ? string.Empty : $"/{Uri.EscapeDataString(reference)}");
        var name = subPath is null ? $"{owner}/{repo}" : $"{owner}/{repo}/{subPath}";
        return new SkillSourceLocation(SkillSourceKind.GitHub, normalized, Limit(name), reference, subPath, archive, $"https://github.com/{owner}/{repo}.git");
    }

    private static SkillSourceLocation GitLab(string[] segments, string normalized)
    {
        // https://gitlab.com/<group>/<subgroup>/<project>/-/tree/<ref>/<path>
        var separator = Array.IndexOf(segments, "-");
        var project = TrimGit(string.Join('/', separator < 0 ? segments : segments[..separator]));
        string? reference = null;
        string? subPath = null;

        if (separator >= 0 && segments.Length > separator + 2 && segments[separator + 1] is "tree" or "blob")
        {
            reference = segments[separator + 2];
            var rest = segments.Skip(separator + 3).ToArray();
            subPath = rest.Length > 0 ? string.Join('/', rest) : null;

            if (segments[separator + 1] == "blob" && subPath is not null && IsMarkdown(subPath))
                return RawFile(normalized, segments) with { DownloadUrl = $"https://gitlab.com/{project}/-/raw/{reference}/{subPath}" };
        }

        var archive = $"https://gitlab.com/api/v4/projects/{Uri.EscapeDataString(project)}/repository/archive.zip"
                      + (reference is null ? string.Empty : $"?sha={Uri.EscapeDataString(reference)}");
        var name = subPath is null ? project : $"{project}/{subPath}";
        return new SkillSourceLocation(SkillSourceKind.GitLab, normalized, Limit(name), reference, subPath, archive, $"https://gitlab.com/{project}.git");
    }

    // The skill is named after the file, or after its folder when the file is a SKILL.md.
    private static SkillSourceLocation RawFile(string normalized, string[] segments)
    {
        var file = segments.Length > 0 ? segments[^1] : "skill.md";
        var stem = Path.GetFileNameWithoutExtension(file);
        var name = stem.Equals("skill", StringComparison.OrdinalIgnoreCase) && segments.Length >= 2 ? segments[^2] : stem;
        return new SkillSourceLocation(SkillSourceKind.RawFile, normalized, Limit(name), null, null, normalized, null);
    }

    private static bool IsMarkdown(string path) =>
        path.EndsWith(".md", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".markdown", StringComparison.OrdinalIgnoreCase);

    private static string TrimGit(string value) =>
        value.EndsWith(".git", StringComparison.OrdinalIgnoreCase) ? value[..^4] : value;

    private static string Limit(string value) => value.Length <= 200 ? value : value[..200];

    [GeneratedRegex(@"^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$")]
    private static partial Regex ShorthandRepo();

    [GeneratedRegex(@"^[\w.-]+@[\w.-]+:(?<path>[\w./-]+)$")]
    private static partial Regex ScpGit();
}

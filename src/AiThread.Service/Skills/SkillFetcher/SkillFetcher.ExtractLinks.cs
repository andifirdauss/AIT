using System.Net;
using System.Text.RegularExpressions;

namespace AiThread.Service.Skills;

public sealed partial class SkillFetcher
{
    // Absolute links to git repositories and Markdown files: what a page listing skills usually points to.
    public static IReadOnlyList<string> ExtractLinks(string html, string pageUrl)
    {
        var baseUri = new Uri(pageUrl);
        var links = new List<string>();
        foreach (Match match in Href().Matches(html))
        {
            if (!Uri.TryCreate(baseUri, WebUtility.HtmlDecode(match.Groups["url"].Value), out var uri)) continue;
            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) continue;
            var host = uri.Host.ToLowerInvariant();
            var path = uri.AbsolutePath;
            var interesting = host is "github.com" or "gitlab.com" && path.Count(c => c == '/') >= 2
                              || path.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
                              || path.EndsWith(".git", StringComparison.OrdinalIgnoreCase);
            if (!interesting) continue;
            var link = uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
            if (!links.Contains(link, StringComparer.OrdinalIgnoreCase)) links.Add(link);
            if (links.Count >= MaxPageLinks) break;
        }
        return links;
    }
}

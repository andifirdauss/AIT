using System.Text;
using System.Text.RegularExpressions;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace AIThread.Service.Business;

// Business pages are Markdown written by the wiki writer (from prompts) and by any developer sharing the store, so
// rendering never trusts it: raw HTML is not parsed, and links may only lead to the web, to mail, within the page, or
// to the page's own images (asset:<id>).
public static partial class BusinessMarkdown
{
    public const string AssetScheme = "asset:";
    public const string AssetRoute = "/api/business/assets/";

    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UsePipeTables()
        .UseBootstrap()
        .DisableHtml()
        .Build();

    public static string ToHtml(string markdown)
    {
        var document = Markdown.Parse(markdown, Pipeline);
        foreach (var link in document.Descendants<LinkInline>()) link.Url = SafeUrl(link.Url, link.IsImage);
        foreach (var link in document.Descendants<AutolinkInline>())
        {
            if (SafeUrl(link.Url, false) == "#") link.Url = "#";
        }

        // Wide tables scroll inside their card instead of stretching the page.
        return document.ToHtml(Pipeline)
            .Replace("<table class=\"table\">", "<div class=\"table-responsive\"><table class=\"table table-sm table-bordered\">", StringComparison.Ordinal)
            .Replace("</table>", "</table></div>", StringComparison.Ordinal)
            .Replace("<img ", "<img loading=\"lazy\" class=\"img-fluid rounded border\" ", StringComparison.Ordinal);
    }

    // The page as the planner reads it: images become their caption, so no binary or URL noise reaches the model.
    public static string ForPlanner(string markdown) =>
        ImagePattern().Replace(markdown, m => $"[gambar: {(m.Groups["alt"].Value is { Length: > 0 } alt ? alt : "screenshot")}]");

    // Ids of the images a page shows.
    public static IReadOnlyList<Guid> AssetIds(string markdown) =>
        AssetPattern().Matches(markdown)
            .Select(m => Guid.TryParse(m.Groups["id"].Value, out var id) ? id : Guid.Empty)
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();

    // "TO", "Transfer Order" → "to-transfer-order".
    public static string Slug(string module, string title)
    {
        var builder = new StringBuilder();
        foreach (var c in $"{module} {title}".Normalize(NormalizationForm.FormD).ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(c)) builder.Append(c);
            else if (char.IsWhiteSpace(c) || c is '-' or '_' or '/' or '.') builder.Append('-');
        }
        var slug = DashesPattern().Replace(builder.ToString(), "-").Trim('-');
        if (slug.Length > 180) slug = slug[..180].TrimEnd('-');
        return slug.Length == 0 ? "page" : slug;
    }

    private static string? SafeUrl(string? url, bool isImage)
    {
        if (string.IsNullOrWhiteSpace(url)) return url;
        var value = url.Trim();

        if (value.StartsWith(AssetScheme, StringComparison.OrdinalIgnoreCase))
            return Guid.TryParse(value[AssetScheme.Length..], out var id) ? AssetRoute + id : "#";

        // Images are only ever the page's own assets; external images would leak who reads the wiki.
        if (isImage) return value.StartsWith(AssetRoute, StringComparison.OrdinalIgnoreCase) ? value : "#";

        if (value.StartsWith('#') || (value.StartsWith('/') && !value.StartsWith("//", StringComparison.Ordinal))) return value;
        return Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" or "mailto" ? value : "#";
    }

    [GeneratedRegex(@"!\[(?<alt>[^\]]*)\]\((?<url>[^)\s]+)(?:\s+""[^""]*"")?\)")]
    private static partial Regex ImagePattern();

    [GeneratedRegex(@"asset:(?<id>[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})")]
    private static partial Regex AssetPattern();

    [GeneratedRegex("-{2,}")]
    private static partial Regex DashesPattern();
}

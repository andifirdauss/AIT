using AiThread.Common.DTOs;
using Markdig;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace AiThread.Service.Implementations;

public sealed partial class DocsService
{
    private static RenderedPage Render(string path, DateTime stamp)
    {
        var document = Markdown.Parse(File.ReadAllText(path), Pipeline);

        foreach (var link in document.Descendants<LinkInline>())
        {
            if (!link.IsImage && link.Url is { } url) link.Url = RewriteLink(url);
        }

        var headings = document.Descendants<HeadingBlock>()
            .Where(h => h.Level is 2 or 3)
            .Select(h => new DocHeadingDto(h.Level, h.GetAttributes().Id ?? string.Empty, PlainText(h.Inline)))
            .Where(h => h.Id.Length > 0)
            .ToList();

        // Wide tables scroll inside their card instead of stretching the page.
        var html = document.ToHtml(Pipeline)
            .Replace("<table class=\"table\">", "<div class=\"table-responsive\"><table class=\"table table-sm\">", StringComparison.Ordinal)
            .Replace("</table>", "</table></div>", StringComparison.Ordinal);

        return new RenderedPage(stamp, html, headings);
    }
}

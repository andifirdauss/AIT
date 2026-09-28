using System.Collections.Concurrent;
using System.Text;
using AiThread.Common.DTOs;
using AiThread.Service.Interfaces;
using Markdig;
using Markdig.Extensions.AutoIdentifiers;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace AiThread.Service.Implementations;

// Renders docs/*.md with Markdig. Links between docs ("02-konfigurasi.md#menu-node") become web routes
// ("/Docs/02-konfigurasi#menu-node"); heading ids follow GitHub's rules so the same anchors work on GitLab and here.
// Pages are re-rendered when their file changes, so editing a doc shows up on the next refresh.
public sealed class DocsService(string? directory) : IDocsService
{
    public const string RoutePrefix = "/Docs";
    private const string IndexFileName = "README.md";

    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UsePipeTables()
        .UseAutoIdentifiers(AutoIdentifierOptions.GitHub)
        .UseBootstrap()
        .Build();

    private readonly ConcurrentDictionary<string, RenderedPage> _cache = new(StringComparer.OrdinalIgnoreCase);

    public string? DirectoryPath { get; } = directory is not null && Directory.Exists(directory) ? Path.GetFullPath(directory) : null;

    // Development runs from src/AiThread.UI, so the repository docs/ folder is two levels up (live edits);
    // a build or publish copies the files next to the binaries.
    public static string? ResolveDirectory(string contentRootPath, string? configuredPath)
    {
        string?[] candidates =
        [
            configuredPath,
            Path.Combine(contentRootPath, "..", "..", "docs"),
            Path.Combine(AppContext.BaseDirectory, "docs"),
            Path.Combine(contentRootPath, "docs")
        ];

        return candidates
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Select(c => Path.GetFullPath(c!))
            .FirstOrDefault(Directory.Exists);
    }

    public IReadOnlyList<DocEntryDto> List()
    {
        if (DirectoryPath is null) return [];

        return Directory.GetFiles(DirectoryPath, "*.md")
            .OrderBy(f => IsIndex(f) ? 0 : 1)
            .ThenBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)
            .Select(f => new DocEntryDto(SlugOf(Path.GetFileName(f)), TitleOf(f)))
            .ToList();
    }

    public DocPageDto? Get(string? slug)
    {
        var entries = List();
        var index = entries.ToList().FindIndex(e => string.Equals(e.Slug, slug ?? string.Empty, StringComparison.OrdinalIgnoreCase));
        if (index < 0) return null;

        var entry = entries[index];
        var path = Path.Combine(DirectoryPath!, entry.Slug.Length == 0 ? IndexFileName : entry.Slug + ".md");
        var stamp = File.GetLastWriteTimeUtc(path);
        if (!_cache.TryGetValue(path, out var page) || page.Stamp != stamp)
        {
            page = Render(path, stamp);
            _cache[path] = page;
        }

        return new DocPageDto(
            entry,
            page.Html,
            page.Headings,
            index > 0 ? entries[index - 1] : null,
            index < entries.Count - 1 ? entries[index + 1] : null);
    }

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

    // "03-integrasi-claude-code.md#cara-b--manual" -> "/Docs/03-integrasi-claude-code#cara-b--manual".
    // Absolute URLs, anchors and paths outside the docs folder are left untouched.
    public static string RewriteLink(string url)
    {
        if (url.Length == 0 || url.StartsWith('#') || url.StartsWith('/') || url.Contains("://", StringComparison.Ordinal)
            || url.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
            return url;

        var hash = url.IndexOf('#');
        var path = hash >= 0 ? url[..hash] : url;
        var fragment = hash >= 0 ? url[hash..] : string.Empty;
        if (!path.EndsWith(".md", StringComparison.OrdinalIgnoreCase) || path.Contains('/') || path.Contains('\\'))
            return url;

        var slug = SlugOf(path);
        return (slug.Length == 0 ? RoutePrefix : $"{RoutePrefix}/{Uri.EscapeDataString(slug)}") + fragment;
    }

    private static bool IsIndex(string path) => string.Equals(Path.GetFileName(path), IndexFileName, StringComparison.OrdinalIgnoreCase);

    private static string SlugOf(string fileName) =>
        string.Equals(fileName, IndexFileName, StringComparison.OrdinalIgnoreCase) ? string.Empty : Path.GetFileNameWithoutExtension(fileName);

    // The first "# " heading, falling back to the file name.
    private static string TitleOf(string path)
    {
        foreach (var line in File.ReadLines(path))
        {
            if (line.StartsWith("# ", StringComparison.Ordinal)) return line[2..].Trim();
        }
        return Path.GetFileNameWithoutExtension(path);
    }

    private static string PlainText(ContainerInline? container)
    {
        if (container is null) return string.Empty;
        var builder = new StringBuilder();
        foreach (var inline in container.Descendants<Inline>())
        {
            switch (inline)
            {
                case LiteralInline literal:
                    builder.Append(literal.Content.ToString());
                    break;
                case CodeInline code:
                    builder.Append(code.Content);
                    break;
            }
        }
        return builder.ToString().Trim();
    }

    private sealed record RenderedPage(DateTime Stamp, string Html, IReadOnlyList<DocHeadingDto> Headings);
}

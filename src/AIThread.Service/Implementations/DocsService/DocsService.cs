using System.Collections.Concurrent;
using AIThread.Common.DTOs;
using AIThread.Service.Interfaces;
using Markdig;
using Markdig.Extensions.AutoIdentifiers;

namespace AIThread.Service.Implementations;

// Renders docs/*.md with Markdig; in English, docs/en/*.md (same file names) where a page is translated. Links between docs ("02-konfigurasi.md#menu-node") become web routes
// ("/Docs/02-konfigurasi#menu-node"); heading ids follow GitHub's rules so the same anchors work on GitLab and here.
// Pages are re-rendered when their file changes, so editing a doc shows up on the next refresh.
public sealed partial class DocsService(string? directory) : IDocsService
{
    public const string RoutePrefix = "/Docs";
    private const string IndexFileName = "README.md";
    private const string EnglishFolder = "en";

    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UsePipeTables()
        .UseAutoIdentifiers(AutoIdentifierOptions.GitHub)
        .UseBootstrap()
        .Build();

    private readonly ConcurrentDictionary<string, RenderedPage> _cache = new(StringComparer.OrdinalIgnoreCase);

    public string? DirectoryPath { get; } = directory is not null && Directory.Exists(directory) ? Path.GetFullPath(directory) : null;

    private sealed record RenderedPage(DateTime Stamp, string Html, IReadOnlyList<DocHeadingDto> Headings);
}

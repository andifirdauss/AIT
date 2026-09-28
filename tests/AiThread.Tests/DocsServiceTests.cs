using AiThread.Service.Implementations;

namespace AiThread.Tests;

public class DocsServiceTests : IDisposable
{
    private readonly TempWorkspace _workspace = new();

    public void Dispose() => _workspace.Dispose();

    [Fact]
    public void Lists_index_first_then_files_by_name_with_titles()
    {
        _workspace.Write("docs/02-b.md", "# 2. Kedua\n");
        _workspace.Write("docs/01-a.md", "Intro tanpa heading\n\n# 1. Pertama\n");
        _workspace.Write("docs/README.md", "# Dokumentasi\n");
        var docs = new DocsService(Path.Combine(_workspace.Root, "docs"));

        var entries = docs.List();

        Assert.Equal(["", "01-a", "02-b"], entries.Select(e => e.Slug));
        Assert.Equal(["Dokumentasi", "1. Pertama", "2. Kedua"], entries.Select(e => e.Title));
    }

    [Fact]
    public void Renders_links_anchors_tables_and_headings()
    {
        _workspace.Write("docs/README.md", "# Index\n\nLihat [konfigurasi](01-konfigurasi.md#menu-node).\n");
        _workspace.Write("docs/01-konfigurasi.md", """
            # 1. Konfigurasi

            ## Menu Node

            | A | B |
            |---|---|
            | 1 | 2 |

            ### Cara B — manual

            Kembali ke [indeks](README.md), situs [luar](https://example.com), [anchor](#menu-node).

            ## Sesi di luar project .sln/.slnx
            """);
        var docs = new DocsService(Path.Combine(_workspace.Root, "docs"));

        var index = docs.Get(null)!;
        Assert.Contains("href=\"/Docs/01-konfigurasi#menu-node\"", index.Html);
        Assert.Null(index.Previous);
        Assert.Equal("01-konfigurasi", index.Next!.Slug);

        var page = docs.Get("01-KONFIGURASI")!;
        Assert.Equal("1. Konfigurasi", page.Entry.Title);
        Assert.Equal(
            [(2, "menu-node", "Menu Node"), (3, "cara-b--manual", "Cara B — manual"), (2, "sesi-di-luar-project-slnslnx", "Sesi di luar project .sln/.slnx")],
            page.Headings.Select(h => (h.Level, h.Id, h.Text)));
        Assert.Contains("<div class=\"table-responsive\"><table class=\"table table-sm\">", page.Html);
        Assert.Contains("href=\"/Docs\"", page.Html);
        Assert.Contains("href=\"https://example.com\"", page.Html);
        Assert.Contains("href=\"#menu-node\"", page.Html);
    }

    [Fact]
    public void Unknown_or_traversal_slugs_are_not_found()
    {
        _workspace.Write("docs/README.md", "# Index\n");
        _workspace.Write("secret.md", "# Secret\n");
        var docs = new DocsService(Path.Combine(_workspace.Root, "docs"));

        Assert.Null(docs.Get("missing"));
        Assert.Null(docs.Get("../secret"));
    }

    [Fact]
    public void Missing_folder_is_reported_as_unavailable()
    {
        var docs = new DocsService(Path.Combine(_workspace.Root, "nope"));

        Assert.Null(docs.DirectoryPath);
        Assert.Empty(docs.List());
        Assert.Null(docs.Get(null));
    }

    [Theory]
    [InlineData("02-konfigurasi.md", "/Docs/02-konfigurasi")]
    [InlineData("README.md#istilah", "/Docs#istilah")]
    [InlineData("../CLAUDE.md", "../CLAUDE.md")]
    [InlineData("image.png", "image.png")]
    public void Rewrites_only_links_between_docs(string url, string expected) =>
        Assert.Equal(expected, DocsService.RewriteLink(url));
}

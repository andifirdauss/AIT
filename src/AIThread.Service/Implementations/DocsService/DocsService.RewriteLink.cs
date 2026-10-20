namespace AIThread.Service.Implementations;

public sealed partial class DocsService
{
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
}

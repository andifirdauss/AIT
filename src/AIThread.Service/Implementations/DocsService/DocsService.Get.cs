using AIThread.Common.DTOs;

namespace AIThread.Service.Implementations;

public sealed partial class DocsService
{
    public DocPageDto? Get(string? slug)
    {
        var entries = List();
        var index = entries.ToList().FindIndex(e => string.Equals(e.Slug, slug ?? string.Empty, StringComparison.OrdinalIgnoreCase));
        if (index < 0) return null;

        var entry = entries[index];
        var path = PathOf(entry.Slug.Length == 0 ? IndexFileName : entry.Slug + ".md");
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
}

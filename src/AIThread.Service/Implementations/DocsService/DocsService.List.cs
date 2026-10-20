using AIThread.Common.DTOs;

namespace AIThread.Service.Implementations;

public sealed partial class DocsService
{
    public IReadOnlyList<DocEntryDto> List()
    {
        if (DirectoryPath is null) return [];

        return Directory.GetFiles(DirectoryPath, "*.md")
            .OrderBy(f => IsIndex(f) ? 0 : 1)
            .ThenBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)
            .Select(f => new DocEntryDto(SlugOf(Path.GetFileName(f)), TitleOf(f)))
            .ToList();
    }
}

namespace AIThread.Common.DTOs;

// Slug is the file name without ".md"; the docs index (README.md) has an empty slug.
public record DocEntryDto(string Slug, string Title);

public record DocHeadingDto(int Level, string Id, string Text);

public record DocPageDto(
    DocEntryDto Entry,
    string Html,
    IReadOnlyList<DocHeadingDto> Headings,
    DocEntryDto? Previous,
    DocEntryDto? Next);

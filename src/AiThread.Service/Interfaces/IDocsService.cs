using AiThread.Common.DTOs;

namespace AiThread.Service.Interfaces;

// The Markdown user guide in the repository's docs/ folder, rendered for the Docs menu.
public interface IDocsService
{
    // Null when no docs folder was found next to the application.
    string? DirectoryPath { get; }

    IReadOnlyList<DocEntryDto> List();

    // Null for an unknown slug. An empty or null slug is the docs index.
    DocPageDto? Get(string? slug);
}

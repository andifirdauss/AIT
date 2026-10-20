using AIThread.Common.DTOs;

namespace AIThread.UI.Models;

public record DocsViewModel(IReadOnlyList<DocEntryDto> Entries, DocPageDto? Page, bool DocsFound);

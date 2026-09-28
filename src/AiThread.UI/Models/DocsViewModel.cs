using AiThread.Common.DTOs;

namespace AiThread.UI.Models;

public record DocsViewModel(IReadOnlyList<DocEntryDto> Entries, DocPageDto? Page, bool DocsFound);

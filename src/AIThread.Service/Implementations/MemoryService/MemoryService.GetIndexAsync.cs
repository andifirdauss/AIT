using AIThread.Common.DTOs;

namespace AIThread.Service.Implementations;

public sealed partial class MemoryService
{
    public Task<IReadOnlyList<MemoryIndexEntry>> GetIndexAsync(Guid? projectId, string? query, int take, CancellationToken cancellationToken = default) =>
        SearchAsync(new MemorySearch(projectId, Text: query, Take: take), cancellationToken);
}

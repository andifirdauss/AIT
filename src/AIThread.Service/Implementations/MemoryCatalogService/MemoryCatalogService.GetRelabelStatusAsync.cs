using AIThread.Common.DTOs;

namespace AIThread.Service.Implementations;

public sealed partial class MemoryCatalogService
{
    public Task<MemoryRelabelStatusDto> GetRelabelStatusAsync(CancellationToken cancellationToken = default) => relabeler.GetStatusAsync(cancellationToken);
}

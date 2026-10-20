using AIThread.Common.DTOs;

namespace AIThread.Service.Implementations;

public sealed partial class MemoryCatalogService
{
    public async Task<IReadOnlyList<ModuleMergeDto>> GetMergesAsync(Guid? projectId, bool knowledge, CancellationToken cancellationToken = default) =>
        await merger.ListAsync(await KeyAsync(projectId, knowledge, cancellationToken), null, 100, cancellationToken);
}

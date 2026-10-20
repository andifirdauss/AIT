using AiThread.Common.DTOs;
using AiThread.Service.Memory;

namespace AiThread.Service.Implementations;

public sealed partial class MemoryCatalogService
{
    public async Task<ModuleMergeDto> MergeAsync(MergeModulesRequest request, bool knowledge, CancellationToken cancellationToken = default)
    {
        var key = await KeyAsync(request.ProjectId, knowledge, cancellationToken);
        var log = await merger.MergeAsync(key, request.From, request.Into, "Digabung manual.", null, await DeciderAsync(cancellationToken), cancellationToken);
        return ModuleMerger.ToDto(log);
    }
}

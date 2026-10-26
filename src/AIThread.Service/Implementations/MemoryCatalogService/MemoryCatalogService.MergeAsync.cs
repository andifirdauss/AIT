using AIThread.Common.DTOs;
using AIThread.Common.Localization;
using AIThread.Service.Memory;

namespace AIThread.Service.Implementations;

public sealed partial class MemoryCatalogService
{
    public async Task<ModuleMergeDto> MergeAsync(MergeModulesRequest request, bool knowledge, CancellationToken cancellationToken = default)
    {
        var key = await KeyAsync(request.ProjectId, knowledge, cancellationToken);
        var log = await merger.MergeAsync(key, request.From, request.Into, Lang.T("Digabung manual.", "Merged manually."), null, await DeciderAsync(cancellationToken), cancellationToken);
        return ModuleMerger.ToDto(log);
    }
}

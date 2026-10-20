using AiThread.Common.DTOs;

namespace AiThread.Service.Implementations;

public sealed partial class NineRouterService
{
    public async Task<IReadOnlyList<RouterModelDto>> GetModelsAsync(bool refresh = false, CancellationToken cancellationToken = default)
    {
        var settings = await settingService.GetRuntimeAsync(cancellationToken);
        return await GetModelsCoreAsync(settings.NineRouterBaseUrl, refresh, cancellationToken);
    }
}

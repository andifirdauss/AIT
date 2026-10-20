using AiThread.Common.DTOs;

namespace AiThread.Service.Implementations;

public sealed partial class NineRouterService
{
    public async Task<RouterStatusDto> CheckStatusAsync(CancellationToken cancellationToken = default)
    {
        var settings = await settingService.GetRuntimeAsync(cancellationToken);
        var status = await ProbeAsync(settings.NineRouterBaseUrl, settings.NineRouterApiKey, useCache: true, cancellationToken);
        LastStatus = status;
        return status;
    }
}

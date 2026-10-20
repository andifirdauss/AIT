using AIThread.Common.DTOs;

namespace AIThread.Service.Implementations;

public sealed partial class NineRouterService
{
    public async Task<RouterStatusDto> TestConnectionAsync(string? baseUrl, string? apiKey, CancellationToken cancellationToken = default)
    {
        var settings = await settingService.GetRuntimeAsync(cancellationToken);
        var url = SettingService.NormalizeRouterUrl(string.IsNullOrWhiteSpace(baseUrl) ? settings.NineRouterBaseUrl : baseUrl);
        var key = string.IsNullOrWhiteSpace(apiKey) ? settings.NineRouterApiKey : apiKey.Trim();
        return await ProbeAsync(url, key, useCache: false, cancellationToken);
    }
}

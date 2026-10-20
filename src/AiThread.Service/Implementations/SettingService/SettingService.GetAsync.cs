using AiThread.Common.DTOs;

namespace AiThread.Service.Implementations;

public sealed partial class SettingService
{
    public async Task<SystemSettingDto> GetAsync(string proxyBaseUrl, CancellationToken cancellationToken = default)
    {
        var entity = await LoadOrCreateAsync(cancellationToken);
        return ToDto(entity, proxyBaseUrl);
    }
}

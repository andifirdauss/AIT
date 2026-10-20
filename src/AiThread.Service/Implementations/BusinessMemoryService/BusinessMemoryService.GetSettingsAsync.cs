using AiThread.Common.DTOs;

namespace AiThread.Service.Implementations;

public sealed partial class BusinessMemoryService
{
    public async Task<BusinessSettingsDto> GetSettingsAsync(CancellationToken cancellationToken = default) =>
        ToSettingsDto(await settingService.GetRuntimeAsync(cancellationToken));
}

using AIThread.Common.DTOs;

namespace AIThread.Service.Implementations;

public sealed partial class BusinessMemoryService
{
    public async Task<BusinessSettingsDto> GetSettingsAsync(CancellationToken cancellationToken = default) =>
        ToSettingsDto(await settingService.GetRuntimeAsync(cancellationToken));
}

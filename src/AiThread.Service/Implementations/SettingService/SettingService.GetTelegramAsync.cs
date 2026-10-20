using AiThread.Common.DTOs;

namespace AiThread.Service.Implementations;

public sealed partial class SettingService
{
    public async Task<TelegramSettingsDto> GetTelegramAsync(CancellationToken cancellationToken = default) =>
        ToTelegramDto(await LoadOrCreateAsync(cancellationToken));
}

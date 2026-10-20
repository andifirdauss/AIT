using AIThread.Common.DTOs;
using AIThread.Common.Entities;

namespace AIThread.Service.Implementations;

public sealed partial class SettingService
{
    private TelegramSettingsDto ToTelegramDto(SystemSetting entity)
    {
        var token = Unprotect(entity.TelegramBotTokenProtected);
        return new TelegramSettingsDto(entity.TelegramEnabled, token is not null, Mask(token));
    }
}

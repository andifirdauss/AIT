using AIThread.Common.DTOs;
using AIThread.Service.Models;
using Microsoft.AspNetCore.DataProtection;

namespace AIThread.Service.Implementations;

public sealed partial class SettingService
{
    public async Task<TelegramSettingsDto> UpdateTelegramAsync(UpdateTelegramSettingsRequest request, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var entity = await LoadTrackedAsync(db, cancellationToken);

        if (request.ClearBotToken)
            entity.TelegramBotTokenProtected = null;
        else if (!string.IsNullOrWhiteSpace(request.BotToken))
            entity.TelegramBotTokenProtected = _protector.Protect(request.BotToken.Trim());

        if (request.Enabled && Unprotect(entity.TelegramBotTokenProtected) is null)
            throw new AppValidationException("Isi bot token dari @BotFather sebelum mengaktifkan Telegram.");

        entity.TelegramEnabled = request.Enabled;
        entity.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
        _cache = ToRuntime(entity);
        return ToTelegramDto(entity);
    }
}

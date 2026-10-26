using AIThread.Common.DTOs;
using AIThread.Common.Localization;
using AIThread.Service.Interfaces;
using AIThread.Service.Models;
using AIThread.Service.Telegram;

namespace AIThread.Service.Implementations;

public sealed partial class TelegramService
{
    public async Task<TelegramStatusDto> UpdateSettingsAsync(UpdateTelegramSettingsRequest request, CancellationToken cancellationToken = default)
    {
        var token = request.ClearBotToken ? null : request.BotToken?.Trim();
        string? botUsername = null;
        if (!string.IsNullOrEmpty(token))
        {
            if (!TelegramApi.IsValidTokenFormat(token))
                throw new AppValidationException(Lang.T("Format bot token tidak valid. Salin token lengkap dari @BotFather, contoh 123456789:AAE…", "Invalid bot token format. Copy the full token from @BotFather, e.g. 123456789:AAE…"));
            try
            {
                botUsername = (await api.GetMeAsync(token, cancellationToken)).Username;
            }
            catch (TelegramApiException ex)
            {
                throw new AppValidationException(ex.IsUnauthorized
                    ? Lang.T("Token ditolak Telegram. Salin ulang token dari @BotFather.", "Telegram rejected the token. Copy the token from @BotFather again.")
                    : Lang.T($"Token tidak dapat diverifikasi: {ex.Message}", $"The token cannot be verified: {ex.Message}"));
            }
        }

        await settingService.UpdateTelegramAsync(request with { BotToken = token }, cancellationToken);
        runtime.Reload();
        activityLog.Info("telegram", request.Enabled
            ? Lang.T($"Pengaturan Telegram disimpan{(botUsername is null ? "" : $" (bot @{botUsername})")}.", $"Telegram settings saved{(botUsername is null ? "" : $" (bot @{botUsername})")}.")
            : Lang.T("Bot Telegram dinonaktifkan.", "Telegram bot turned off."));
        return await GetStatusAsync(cancellationToken);
    }
}

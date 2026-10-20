using AiThread.Common.DTOs;
using AiThread.Service.Interfaces;
using AiThread.Service.Models;
using AiThread.Service.Telegram;

namespace AiThread.Service.Implementations;

public sealed partial class TelegramService
{
    public async Task<TelegramStatusDto> UpdateSettingsAsync(UpdateTelegramSettingsRequest request, CancellationToken cancellationToken = default)
    {
        var token = request.ClearBotToken ? null : request.BotToken?.Trim();
        string? botUsername = null;
        if (!string.IsNullOrEmpty(token))
        {
            if (!TelegramApi.IsValidTokenFormat(token))
                throw new AppValidationException("Format bot token tidak valid. Salin token lengkap dari @BotFather, contoh 123456789:AAE…");
            try
            {
                botUsername = (await api.GetMeAsync(token, cancellationToken)).Username;
            }
            catch (TelegramApiException ex)
            {
                throw new AppValidationException(ex.IsUnauthorized
                    ? "Token ditolak Telegram. Salin ulang token dari @BotFather."
                    : $"Token tidak dapat diverifikasi: {ex.Message}");
            }
        }

        await settingService.UpdateTelegramAsync(request with { BotToken = token }, cancellationToken);
        runtime.Reload();
        activityLog.Info("telegram", request.Enabled
            ? $"Setting Telegram disimpan{(botUsername is null ? "" : $" (bot @{botUsername})")}."
            : "Bot Telegram dinonaktifkan.");
        return await GetStatusAsync(cancellationToken);
    }
}

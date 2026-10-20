using AIThread.Service.Models;

namespace AIThread.Service.Telegram;

public sealed partial class TelegramApi
{
    private async Task<string> TokenAsync(CancellationToken cancellationToken) =>
        (await settingService.GetRuntimeAsync(cancellationToken)).TelegramBotToken
        ?? throw new TelegramApiException("Bot token Telegram belum diisi (menu Setting).");
}

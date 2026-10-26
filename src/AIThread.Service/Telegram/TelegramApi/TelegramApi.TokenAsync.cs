using AIThread.Common.Localization;
using AIThread.Service.Models;

namespace AIThread.Service.Telegram;

public sealed partial class TelegramApi
{
    private async Task<string> TokenAsync(CancellationToken cancellationToken) =>
        (await settingService.GetRuntimeAsync(cancellationToken)).TelegramBotToken
        ?? throw new TelegramApiException(Lang.T("Bot token Telegram belum diisi (menu Pengaturan).", "The Telegram bot token is not filled in yet (Setting menu)."));
}

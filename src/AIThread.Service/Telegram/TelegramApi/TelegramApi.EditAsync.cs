using System.Text.Json;
using AIThread.Service.Models;

namespace AIThread.Service.Telegram;

public sealed partial class TelegramApi
{
    // Replaces the text of a message the bot sent. A null keyboard removes the buttons. "Not modified" is not an error.
    public async Task EditAsync(long chatId, long messageId, string html, TgInlineKeyboard? keyboard = null, CancellationToken cancellationToken = default)
    {
        var token = await TokenAsync(cancellationToken);
        try
        {
            await CallFormattedAsync<JsonElement>(token, "editMessageText", asHtml => new
            {
                ChatId = chatId,
                MessageId = messageId,
                Text = asHtml ? html : TelegramFormatter.ToPlainText(html),
                ParseMode = asHtml ? "HTML" : null,
                LinkPreviewOptions = new { IsDisabled = true },
                ReplyMarkup = keyboard
            }, cancellationToken);
        }
        catch (TelegramApiException ex) when (ex.IsNotModified)
        {
        }
    }
}

namespace AiThread.Service.Telegram;

public sealed partial class TelegramApi
{
    // Returns the id of the sent message.
    public async Task<long> SendAsync(long chatId, string html, long? replyToMessageId = null, TgInlineKeyboard? keyboard = null,
        CancellationToken cancellationToken = default)
    {
        var token = await TokenAsync(cancellationToken);
        var message = await CallFormattedAsync<TgMessage>(token, "sendMessage", asHtml => new
        {
            ChatId = chatId,
            Text = asHtml ? html : TelegramFormatter.ToPlainText(html),
            ParseMode = asHtml ? "HTML" : null,
            LinkPreviewOptions = new { IsDisabled = true },
            ReplyParameters = replyToMessageId is { } id ? new { MessageId = id, AllowSendingWithoutReply = true } : null,
            ReplyMarkup = keyboard
        }, cancellationToken);
        return message.MessageId;
    }
}

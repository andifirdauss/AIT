namespace AIThread.Service.Telegram;

public sealed partial class TelegramApi
{
    // Stops the loading indicator on the pressed button; text is shown as a short toast.
    public async Task AnswerCallbackAsync(string callbackQueryId, string? text, CancellationToken cancellationToken) =>
        await CallAsync<bool>(await TokenAsync(cancellationToken), "answerCallbackQuery",
            new { CallbackQueryId = callbackQueryId, Text = text }, CallTimeout, cancellationToken);
}

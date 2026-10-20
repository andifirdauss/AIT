using AiThread.Service.Telegram;

namespace AiThread.Service.Interfaces;

public interface ITelegramApi
{
    Task<TgUser> GetMeAsync(string token, CancellationToken cancellationToken);

    Task DeleteWebhookAsync(CancellationToken cancellationToken);

    Task SetCommandsAsync(IEnumerable<TelegramCommand> commands, CancellationToken cancellationToken);

    Task<IReadOnlyList<TgUpdate>> GetUpdatesAsync(long offset, int timeoutSeconds, CancellationToken cancellationToken);

    Task<long> SendAsync(long chatId, string html, long? replyToMessageId = null, TgInlineKeyboard? keyboard = null, CancellationToken cancellationToken = default);

    Task EditAsync(long chatId, long messageId, string html, TgInlineKeyboard? keyboard = null, CancellationToken cancellationToken = default);

    Task AnswerCallbackAsync(string callbackQueryId, string? text, CancellationToken cancellationToken);
}

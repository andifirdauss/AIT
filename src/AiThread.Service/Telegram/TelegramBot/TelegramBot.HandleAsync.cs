using Microsoft.Extensions.Logging;

namespace AiThread.Service.Telegram;

public sealed partial class TelegramBot
{
    public async Task HandleAsync(TgUpdate update, DateTime pollingStartedAt, CancellationToken cancellationToken)
    {
        if (update.CallbackQuery is { } callback)
        {
            await HandleButtonAsync(callback, cancellationToken);
            return;
        }

        if (update.Message is not { } message) return;
        if (message.Chat is not { Type: "private" })
        {
            // A group would let every member run prompts on this machine.
            logger.LogDebug("Ignoring Telegram message from non-private chat {ChatId}.", message.Chat?.Id);
            return;
        }

        var chat = await TouchChatAsync(message.Chat, cancellationToken);
        if (!chat.IsAllowed)
        {
            await ReplyNotAllowedAsync(chat, cancellationToken);
            return;
        }

        // Allowed now: if access is revoked later, the next message must be answered right away.
        _deniedAt.TryRemove(chat.ChatId, out _);

        try
        {
            await HandleAllowedMessageAsync(chat, message, pollingStartedAt, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Telegram message {MessageId} from chat {ChatId} failed.", message.MessageId, chat.ChatId);
            await TrySendAsync(chat.ChatId, TelegramMessages.Failure(ex.Message), cancellationToken);
        }
    }
}

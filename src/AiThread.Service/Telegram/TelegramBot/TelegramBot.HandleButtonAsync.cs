using Microsoft.Extensions.Logging;

namespace AiThread.Service.Telegram;

public sealed partial class TelegramBot
{
    private async Task HandleButtonAsync(TgCallbackQuery callback, CancellationToken cancellationToken)
    {
        var chat = await FindChatAsync(callback.Message?.Chat?.Id ?? callback.From.Id, cancellationToken);
        if (chat is not { IsAllowed: true })
        {
            await api.AnswerCallbackAsync(callback.Id, "Chat ini belum diizinkan.", cancellationToken);
            return;
        }

        try
        {
            if (TelegramMessages.TryParseButton(callback.Data, TelegramMessages.ProjectButton, out var projectId))
                await SelectProjectFromButtonAsync(chat, callback, projectId, cancellationToken);
            else if (TelegramMessages.TryParseButton(callback.Data, TelegramMessages.CancelButton, out var threadId))
                await api.AnswerCallbackAsync(callback.Id, threadService.Cancel(threadId) ? "Pembatalan dikirim." : "Thread sudah selesai.", cancellationToken);
            else
                await api.AnswerCallbackAsync(callback.Id, null, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Telegram button {Data} from chat {ChatId} failed.", callback.Data, chat.ChatId);
            await TrySendAsync(chat.ChatId, TelegramMessages.Failure(ex.Message), cancellationToken);
        }
    }
}

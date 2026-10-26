using AIThread.Common.Localization;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.Telegram;

public sealed partial class TelegramBot
{
    private async Task HandleButtonAsync(TgCallbackQuery callback, CancellationToken cancellationToken)
    {
        var chat = await FindChatAsync(callback.Message?.Chat?.Id ?? callback.From.Id, cancellationToken);
        if (chat is not { IsAllowed: true })
        {
            await api.AnswerCallbackAsync(callback.Id, Lang.T("Chat ini belum diizinkan.", "This chat is not allowed yet."), cancellationToken);
            return;
        }

        try
        {
            if (TelegramMessages.TryParseButton(callback.Data, TelegramMessages.ProjectButton, out var projectId))
                await SelectProjectFromButtonAsync(chat, callback, projectId, cancellationToken);
            else if (TelegramMessages.TryParseButton(callback.Data, TelegramMessages.CancelButton, out var threadId))
                await api.AnswerCallbackAsync(callback.Id, threadService.Cancel(threadId) ? Lang.T("Pembatalan dikirim.", "Cancellation sent.") : Lang.T("Thread sudah selesai.", "The thread already finished."), cancellationToken);
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

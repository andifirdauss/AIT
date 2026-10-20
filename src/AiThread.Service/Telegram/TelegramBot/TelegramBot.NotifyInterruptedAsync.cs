using AiThread.Common.Enums;
using Microsoft.Extensions.Logging;

namespace AiThread.Service.Telegram;

public sealed partial class TelegramBot
{
    // AiThread is stopping mid-thread: without this the chat would wait for an answer that never comes.
    // Skipped when the thread had already finished and only sending its result was cut short.
    private async Task NotifyInterruptedAsync(TelegramThreadContext context)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            var status = (await threadService.GetDetailAsync(context.ThreadId, timeout.Token))?.Summary.Status;
            if (status is ThreadStatus.Completed or ThreadStatus.Failed) return;
            await api.SendAsync(context.ChatId, TelegramMessages.Interrupted(context.ThreadId), context.PromptMessageId, cancellationToken: timeout.Token);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Could not tell Telegram chat {ChatId} that thread {ThreadId} was interrupted.", context.ChatId, context.ThreadId);
        }
    }
}

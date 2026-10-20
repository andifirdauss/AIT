using AiThread.Data.Messaging;
using AiThread.Service.Models;
using Microsoft.Extensions.Logging;

namespace AiThread.Service.Telegram;

public sealed partial class TelegramThreadReporter
{
    public async Task RunAsync(TelegramThreadContext context, IEventSubscription subscription, CancellationToken cancellationToken)
    {
        long? statusMessageId = context.StatusMessageId;
        string? plannerActivity = null;
        string? shown = null;
        var nextEditAt = DateTime.MinValue;

        while (true)
        {
            plannerActivity = await WaitForChangeAsync(subscription, context.ThreadId, plannerActivity, cancellationToken);

            var wait = nextEditAt - DateTime.UtcNow;
            if (wait > TimeSpan.Zero)
            {
                await Task.Delay(wait, cancellationToken);
                plannerActivity = Drain(subscription, context.ThreadId, plannerActivity).Activity;
            }

            var detail = await threadService.GetDetailAsync(context.ThreadId, cancellationToken);
            if (detail is null) return;
            var finished = !TelegramMessages.IsActive(detail.Summary.Status);

            if (statusMessageId is { } messageId)
            {
                var text = TelegramMessages.ThreadProgress(detail, context.ProjectName, finished ? null : plannerActivity, DateTime.UtcNow);
                if (text != shown)
                {
                    try
                    {
                        await api.EditAsync(context.ChatId, messageId, text, finished ? null : TelegramMessages.CancelKeyboard(context.ThreadId), cancellationToken);
                        shown = text;
                    }
                    catch (TelegramApiException ex) when (ex.IsMessageGone)
                    {
                        // Deleted by the user; the answer still arrives as a new message.
                        statusMessageId = null;
                    }
                    catch (TelegramApiException ex) when (ex.IsChatUnreachable)
                    {
                        logger.LogInformation("Telegram chat {ChatId} is unreachable; stopped reporting thread {ThreadId}.", context.ChatId, context.ThreadId);
                        return;
                    }
                    catch (TelegramApiException ex)
                    {
                        logger.LogDebug(ex, "Telegram status update for thread {ThreadId} failed.", context.ThreadId);
                    }
                    nextEditAt = DateTime.UtcNow + MinEditInterval;
                }
            }

            if (finished)
            {
                await SendResultAsync(context, detail, cancellationToken);
                return;
            }
        }
    }
}

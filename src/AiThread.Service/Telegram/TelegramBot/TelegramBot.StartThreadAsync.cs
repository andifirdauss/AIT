using AiThread.Common.DTOs;
using AiThread.Common.Entities;
using AiThread.Common.Enums;
using AiThread.Service.Models;
using Microsoft.Extensions.Logging;

namespace AiThread.Service.Telegram;

public sealed partial class TelegramBot
{
    private async Task StartThreadAsync(TelegramChat chat, long promptMessageId, string prompt, CancellationToken cancellationToken)
    {
        // One thread per chat at a time: a second prompt would race the first one on the same files.
        if (_running.TryGetValue(chat.ChatId, out var runningId))
        {
            await api.SendAsync(chat.ChatId, TelegramMessages.StillRunning(runningId), promptMessageId, cancellationToken: cancellationToken);
            return;
        }

        var project = await ResolveProjectAsync(chat, cancellationToken);
        if (project is null)
        {
            _pending[chat.ChatId] = new PendingPrompt(prompt, promptMessageId, DateTime.UtcNow);
            await SendProjectMenuAsync(chat, TelegramMessages.PickProjectForPrompt, cancellationToken);
            return;
        }

        var conversation = await BuildConversationAsync(chat, project.Id, cancellationToken);
        var statusMessageId = await api.SendAsync(chat.ChatId, TelegramMessages.Starting(project.Name), promptMessageId, cancellationToken: cancellationToken);

        // Subscribe before starting: the first events fire before StartInBackgroundAsync returns the thread id.
        var subscription = eventBus.Subscribe(evt => evt.ThreadId is not null &&
                                                     evt.Type is AppEventTypes.ThreadUpdated or AppEventTypes.TaskUpdated or AppEventTypes.ThreadOutput);
        Guid threadId;
        try
        {
            threadId = await orchestrator.StartInBackgroundAsync(new ThreadRequest(
                ThreadSource.Telegram,
                prompt,
                PromptContext: null,
                conversation,
                WorkingDirectory: null,
                project.Id,
                SessionIdFor(chat.ChatId)), cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            subscription.Dispose();
            logger.LogWarning(ex, "Telegram prompt from chat {ChatId} could not start a thread.", chat.ChatId);
            await api.EditAsync(chat.ChatId, statusMessageId, TelegramMessages.StartFailed(ex.Message), cancellationToken: cancellationToken);
            return;
        }

        _running[chat.ChatId] = threadId;
        var context = new TelegramThreadContext(chat.ChatId, promptMessageId, statusMessageId, threadId, project.Name);
        _ = Task.Run(async () =>
        {
            try
            {
                await reporter.RunAsync(context, subscription, lifetime.ApplicationStopping);
            }
            catch (OperationCanceledException)
            {
                await NotifyInterruptedAsync(context);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Reporting thread {ThreadId} to Telegram failed.", threadId);
            }
            finally
            {
                subscription.Dispose();
                _running.TryRemove(new KeyValuePair<long, Guid>(chat.ChatId, threadId));
            }
        }, CancellationToken.None);
    }
}

using AIThread.Common.Entities;

namespace AIThread.Service.Telegram;

public sealed partial class TelegramBot
{
    // No argument: this chat's running thread. With an id prefix: any running thread, e.g. one from Claude Code.
    private async Task CancelAsync(TelegramChat chat, string argument, CancellationToken cancellationToken)
    {
        if (argument.Length == 0)
        {
            var reply = _running.TryGetValue(chat.ChatId, out var id)
                ? threadService.Cancel(id) ? TelegramMessages.CancelSent(id) : TelegramMessages.NotRunning(id)
                : TelegramMessages.NothingToCancel;
            await api.SendAsync(chat.ChatId, reply, cancellationToken: cancellationToken);
            return;
        }

        var match = await FindThreadAsync(argument, activeOnly: true, cancellationToken);
        await api.SendAsync(chat.ChatId,
            match is { } threadId && threadService.Cancel(threadId) ? TelegramMessages.CancelSent(threadId) : TelegramMessages.NoRunningMatch(argument),
            cancellationToken: cancellationToken);
    }
}

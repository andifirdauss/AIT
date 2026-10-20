using AIThread.Common.Entities;

namespace AIThread.Service.Telegram;

public sealed partial class TelegramBot
{
    private async Task SendDiffAsync(TelegramChat chat, string argument, CancellationToken cancellationToken)
    {
        var threadId = argument.Length > 0
            ? await FindThreadAsync(argument, activeOnly: false, cancellationToken)
            : await LastChatThreadAsync(chat.ChatId, cancellationToken);
        var detail = threadId is { } id ? await threadService.GetDetailAsync(id, cancellationToken) : null;
        if (detail is null)
        {
            await api.SendAsync(chat.ChatId, argument.Length > 0 ? TelegramMessages.ThreadNotFound(argument) : TelegramMessages.NoThreadYet,
                cancellationToken: cancellationToken);
            return;
        }

        var changed = detail.Tasks.Where(t => !string.IsNullOrWhiteSpace(t.Diff)).ToList();
        if (changed.Count == 0)
        {
            await api.SendAsync(chat.ChatId, TelegramMessages.NoChanges(detail.Summary.Id), cancellationToken: cancellationToken);
            return;
        }

        var blocks = new List<string> { TelegramMessages.DiffHeader(detail.Summary.Id, changed.Count) };
        foreach (var task in changed)
        {
            blocks.Add(string.Empty);
            blocks.Add(TelegramMessages.DiffTask(task));
            blocks.AddRange(TelegramFormatter.CodeBlocks(task.Diff!.ReplaceLineEndings("\n").Split('\n'), "diff"));
        }

        var chunks = TelegramFormatter.Pack(blocks);
        foreach (var chunk in chunks.Take(MaxDiffMessages)) await api.SendAsync(chat.ChatId, chunk, cancellationToken: cancellationToken);
        if (chunks.Count > MaxDiffMessages) await api.SendAsync(chat.ChatId, TelegramMessages.DiffTruncated, cancellationToken: cancellationToken);
    }
}

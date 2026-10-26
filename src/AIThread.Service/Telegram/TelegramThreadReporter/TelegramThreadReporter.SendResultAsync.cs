using AIThread.Common.DTOs;
using AIThread.Common.Localization;

namespace AIThread.Service.Telegram;

public sealed partial class TelegramThreadReporter
{
    private async Task SendResultAsync(TelegramThreadContext context, ThreadDetailDto detail, CancellationToken cancellationToken)
    {
        var blocks = new List<string> { TelegramMessages.ResultHeader(detail, context.ProjectName) };
        if (!string.IsNullOrWhiteSpace(detail.FinalResponse))
        {
            blocks.Add(string.Empty);
            blocks.AddRange(TelegramFormatter.ToBlocks(detail.FinalResponse));
        }
        if (detail.Tasks.Any(t => !string.IsNullOrWhiteSpace(t.Diff)))
        {
            blocks.Add(string.Empty);
            blocks.Add(Lang.T("<i>Kirim /diff untuk melihat perubahan file.</i>", "<i>Send /diff to see the file changes.</i>"));
        }

        var chunks = TelegramFormatter.Pack(blocks);
        long? replyTo = context.PromptMessageId;
        foreach (var chunk in chunks.Take(MaxResultMessages))
        {
            await api.SendAsync(context.ChatId, chunk, replyTo, cancellationToken: cancellationToken);
            replyTo = null;
        }
        if (chunks.Count > MaxResultMessages)
            await api.SendAsync(context.ChatId,
                Lang.T($"… {chunks.Count - MaxResultMessages} bagian lagi tidak dikirim. Lihat detail thread di dashboard AIT.", $"… {chunks.Count - MaxResultMessages} more parts not sent. See the thread detail in the AIT dashboard."), cancellationToken: cancellationToken);
    }
}

using AIThread.Common.Entities;
using AIThread.Common.Enums;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Telegram;

public sealed partial class TelegramBot
{
    // Earlier prompts and answers of this chat in this project since the last /new, formatted like the Claude Code
    // history the planner already knows. Model-facing text is English.
    private async Task<string?> BuildConversationAsync(TelegramChat chat, Guid projectId, CancellationToken cancellationToken)
    {
        var sessionId = SessionIdFor(chat.ChatId);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var threads = await db.Threads.AsNoTracking()
            .Where(t => t.SessionId == sessionId && t.ProjectId == projectId && t.CreatedAt >= chat.ConversationStartedAt && t.CompletedAt != null)
            .OrderByDescending(t => t.CreatedAt)
            .Take(HistoryThreads)
            .Select(t => new { t.Prompt, t.FinalResponse, t.Error, t.Status })
            .ToListAsync(cancellationToken);
        if (threads.Count == 0) return null;

        var parts = new List<string>();
        foreach (var thread in Enumerable.Reverse(threads))
        {
            var answer = thread.FinalResponse
                         ?? (thread.Status == ThreadStatus.Cancelled ? "(the thread was cancelled)" : $"(the thread failed: {thread.Error})");
            parts.Add($"[user] {Clip(thread.Prompt)}");
            parts.Add($"[assistant] {Clip(answer)}");
        }

        var history = string.Join("\n\n", parts);
        return history.Length <= HistoryChars ? history : "…" + history[^HistoryChars..];

        static string Clip(string text) => text.Length > HistoryMessageChars ? text[..HistoryMessageChars] + " …" : text;
    }
}

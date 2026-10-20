using System.Text;
using System.Text.Json.Nodes;
using AiThread.Common.DTOs;
using AiThread.Common.Enums;
using AiThread.Data.Context;
using AiThread.Data.Messaging;
using Microsoft.EntityFrameworkCore;

namespace AiThread.Service.Orchestration;

public sealed record QuestionOption(string Label, string? Description);

// A clarifying question from the planner's ask_user tool, in the developer's language.
public sealed record PlannerQuestion(string Question, IReadOnlyList<QuestionOption> Options)
{
    // Shown to the developer (Claude Code, Codex, Gemini CLI, Telegram) as the thread's reply.
    public string ToText()
    {
        var builder = new StringBuilder("❓ **Pertanyaan dari planner**\n\n").Append(Question.Trim()).Append('\n');
        if (Options.Count > 0)
        {
            builder.Append('\n');
            for (var i = 0; i < Options.Count; i++)
            {
                var option = Options[i];
                builder.Append(i + 1).Append(". **").Append(option.Label.Trim()).Append("**");
                if (!string.IsNullOrWhiteSpace(option.Description)) builder.Append(" — ").Append(option.Description.Trim());
                builder.Append('\n');
            }
            builder.Append("\nBalas dengan nomor pilihan atau tulis jawaban Anda sendiri.");
        }
        else
        {
            builder.Append("\nBalas pesan ini untuk melanjutkan.");
        }
        return builder.ToString();
    }

    // What the planner receives as the result of ask_user. A bare option number is spelled out so the planner does
    // not have to map it back itself.
    public string AnswerFor(string reply)
    {
        var text = reply.Trim();
        var number = text.TrimEnd('.', ')');
        if (int.TryParse(number, out var index) && index >= 1 && index <= Options.Count)
        {
            var option = Options[index - 1];
            return $"The developer chose option {index}: {option.Label}" +
                   (string.IsNullOrWhiteSpace(option.Description) ? "." : $" — {option.Description}.");
        }
        return $"The developer replied (it may answer your question or change the request):\n{text}";
    }
}

// Everything needed to continue a planner conversation after the developer answers. Messages end with the
// assistant turn that called ask_user plus the results of the other tools called in that turn.
public sealed record PlannerConversation(
    JsonArray Messages,
    Guid NodeId,
    int Step,
    int Tokens,
    int CachedTokens,
    string AskCallId,
    int QuestionsAsked,
    bool CanBuild,
    int ToolsNodeTokens = 0);

public sealed record PendingQuestion(
    Guid ThreadId,
    Guid ProjectId,
    string SessionId,
    PlannerQuestion Question,
    PlannerConversation Conversation,
    DateTime ExpiresAt);

// Closes threads whose planner question will not be answered any more (expired, cancelled, replaced, or lost in a
// restart). Only threads still waiting are touched, so a thread that resumed in the meantime is left alone.
public static class UnansweredQuestions
{
    public static async Task<int> CloseAsync(IDbContextFactory<AiThreadDbContext> dbFactory, IEventBus eventBus, IReadOnlyCollection<Guid> threadIds, string reason)
    {
        if (threadIds.Count == 0) return 0;

        var now = DateTime.UtcNow;
        await using var db = await dbFactory.CreateDbContextAsync();
        var closed = await db.Threads
            .Where(t => threadIds.Contains(t.Id) && t.Status == ThreadStatus.AwaitingInput)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.Status, ThreadStatus.Cancelled)
                .SetProperty(t => t.Error, reason)
                .SetProperty(t => t.CompletedAt, now)
                .SetProperty(t => t.UpdatedAt, now));

        foreach (var id in threadIds)
            eventBus.Publish(AppEvent.Create(AppEventTypes.ThreadUpdated, new { Id = id, Status = ThreadStatus.Cancelled, Error = reason }, id));
        return closed;
    }
}

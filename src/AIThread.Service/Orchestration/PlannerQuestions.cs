using System.Text.Json.Nodes;
using System.Text;
using AIThread.Common.DTOs;
using AIThread.Common.Enums;
using AIThread.Common.Localization;
using AIThread.Data.Context;
using AIThread.Data.Messaging;
using AIThread.Service.Approvals;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Orchestration;

public sealed record QuestionOption(string Label, string? Description);

// A run_command line waiting for the developer's permission, with the folder it runs in. Background: a start_process
// instead, with what it waits for (Wait) before the planner goes on.
public sealed record PendingCommand(string Command, string WorkingDirectory, string? Reason, bool Background = false, Workspace.Commands.BackgroundWait? Wait = null);

// A web_fetch URL on an outside host waiting for the developer's permission; MaxChars as the planner asked.
public sealed record PendingFetch(string Url, string? Reason, int? MaxChars = null);

// What the planner waits for permission to do: run a command, read a page on an outside host (Fetch), or carry out a
// plan that changes files outside the project root (Plan holds it, already validated). Request is what the developer sees.
public sealed record PlannerApproval(ApprovalRequest Request, ThreadPlan? Plan = null, PendingCommand? Command = null, PendingFetch? Fetch = null);

// A clarifying question from the planner's ask_user tool, in the developer's language; or, with Approval, a request for
// the developer's permission (not counted as a question).
public sealed record PlannerQuestion(string Question, IReadOnlyList<QuestionOption> Options, PlannerApproval? Approval = null)
{
    public static PlannerQuestion ForApproval(PlannerApproval approval) => new(approval.Request.Reason, [], approval);

    // Shown to the developer (Claude Code, Codex, Gemini CLI, Telegram) as the thread's reply.
    public string ToText()
    {
        if (Approval is not null) return Approval.Request.ToText();
        var builder = new StringBuilder(Lang.T("❓ **Pertanyaan dari planner**\n\n", "❓ **Question from the planner**\n\n")).Append(Question.Trim()).Append('\n');
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
            builder.Append(Lang.T("\nBalas dengan nomor pilihan atau tulis jawaban Anda sendiri.", "\nReply with the option number or write your own answer."));
        }
        else
        {
            builder.Append(Lang.T("\nBalas pesan ini untuk melanjutkan.", "\nReply to this message to continue."));
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
    int ToolsNodeTokens = 0,
    bool CanGit = false);

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
    public static async Task<int> CloseAsync(IDbContextFactory<AIThreadDbContext> dbFactory, IEventBus eventBus, IReadOnlyCollection<Guid> threadIds, string reason)
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

using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text;
using AIThread.Common.DTOs;
using AIThread.Common.Enums;
using AIThread.Common.Localization;
using AIThread.UI.Hosting.Chat;

namespace AIThread.UI.Hosting.Agent;

public sealed record ThreadReportFile(string File, string Action, ThreadTaskStatus Status, string? Error);

public sealed record ThreadReportCheck(string Name, string Outcome, string? Text);

// Outcome: completed, failed, waiting (WaitingFor: approval or question) or running. A planner's question is in
// FinalResponse.
public sealed record ThreadReport(Guid ThreadId, string? SessionId, ThreadStatus Status, string Outcome, string? WaitingFor, string? FinalResponse, string? Error,
    IReadOnlyList<ThreadReportFile> Files, IReadOnlyList<ThreadReportCheck> Checks, PendingApprovalDto? Approval, string Url);

// aithread thread wait / approve: following a thread started with chat --detach. A coding CLI's shell tool has a time
// limit, and not every CLI can leave a command in the background; waiting in short repeatable steps works in all of
// them, and the thread itself runs on the server regardless.
[SupportedOSPlatform("windows")]
public static class ThreadCommand
{
    private const int DefaultTimeoutSeconds = 240;
    private static readonly TimeSpan PollEvery = TimeSpan.FromSeconds(2);

    private static string Usage => Lang.T("Pemakaian: AIThread.exe thread wait <id> [--timeout detik] [--json] | thread approve <id> once|always|deny [--json]",
        "Usage: AIThread.exe thread wait <id> [--timeout seconds] [--json] | thread approve <id> once|always|deny [--json]");

    public static async Task<int> RunAsync(int port, IReadOnlyList<string> rest, int? timeout, bool json)
    {
        Console.OutputEncoding = Encoding.UTF8;
        var action = rest.FirstOrDefault()?.ToLowerInvariant();
        if (rest.Count < 2 || !Guid.TryParse(rest[1], out var threadId) || (action, rest.Count) is not (("wait", 2) or ("approve", 3)))
            return Refuse(Usage);

        ApprovalDecision? decision = null;
        if (action == "approve" && (decision = ParseDecision(rest[2])) is null) return Refuse(Usage);

        using var api = new ChatApiClient(port);
        try
        {
            return decision is { } answer
                ? await ApproveAsync(api, threadId, answer, json)
                : await WaitAsync(api, threadId, TimeSpan.FromSeconds(timeout ?? DefaultTimeoutSeconds), json);
        }
        catch (ChatApiException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return ChatCommand.Failed;
        }
    }

    public static ApprovalDecision? ParseDecision(string text) => text.ToLowerInvariant() switch
    {
        "once" => ApprovalDecision.Once,
        "always" => ApprovalDecision.Always,
        "deny" => ApprovalDecision.Deny,
        _ => null
    };

    public static int ExitCodeOf(ThreadStatus status, bool waiting, bool timedOut) =>
        waiting ? ChatCommand.AwaitingApproval
        : timedOut ? ChatCommand.StillRunning
        : status == ThreadStatus.Completed ? ChatCommand.Ok
        : ChatCommand.Failed;

    public static ThreadReport ReportOf(ThreadDetailDto detail, string url, bool timedOut, PendingApprovalDto? approval, bool question)
    {
        var waiting = approval is not null || question;
        var outcome = waiting ? "waiting" : timedOut ? "running" : detail.Summary.Status == ThreadStatus.Completed ? "completed" : "failed";
        return new ThreadReport(detail.Summary.Id, detail.Summary.SessionId, detail.Summary.Status, outcome,
            approval is not null ? "approval" : question ? "question" : null,
            detail.FinalResponse, detail.Error,
            detail.Tasks.OrderBy(t => t.Order).Select(t => new ThreadReportFile(ChatRenderer.FileOf(t), t.Action, t.Status, t.Error)).ToList(),
            ChatRenderer.Checks(detail).Select(c => new ThreadReportCheck(c.Name, c.Outcome, c.Text)).ToList(),
            approval, url);
    }

    private static async Task<int> WaitAsync(ChatApiClient api, Guid threadId, TimeSpan limit, bool json)
    {
        var renderer = ChatRenderer.ForConsole(forcePlain: true);
        renderer.Quiet = true;
        var follower = new ThreadFollower(api, renderer);
        var clock = Stopwatch.StartNew();

        while (true)
        {
            var detail = await api.GetThreadAsync(threadId, CancellationToken.None);
            if (!ChatThreadState.IsFinalStatus(detail.Summary.Status))
            {
                if (clock.Elapsed >= limit) return Finish(api, renderer, detail, json, timedOut: true, null, false);
                await Task.Delay(PollEvery);
                continue;
            }

            var approval = await follower.PendingApprovalAsync(detail, interactive: false, CancellationToken.None);
            if (approval is not null) return Finish(api, renderer, detail, json, timedOut: false, approval, false);
            if (detail.Summary.Status != ThreadStatus.AwaitingInput) return Finish(api, renderer, detail, json, timedOut: false, null, false);

            // Just answered: the planner resumes within moments, while a real question keeps this status.
            var settled = await api.GetThreadAsync(threadId, CancellationToken.None);
            if (settled.Summary.Status == ThreadStatus.AwaitingInput) return Finish(api, renderer, settled, json, timedOut: false, null, true);
        }
    }

    private static async Task<int> ApproveAsync(ChatApiClient api, Guid threadId, ApprovalDecision decision, bool json)
    {
        if (await api.GetApprovalAsync(threadId, CancellationToken.None) is not { } approval)
            return Refuse(Lang.T("Thread ini tidak sedang menunggu izin.", "This thread is not waiting for permission."), ChatCommand.Failed);

        var next = await api.ApproveAsync(threadId, decision, CancellationToken.None);
        if (!approval.AfterThread)
        {
            if (json) AgentOutput.Write(new { threadId = next, continues = true, url = api.ThreadUrl(next) });
            else Console.WriteLine(Lang.T($"Thread berlanjut: {next}", $"The thread goes on: {next}"));
            return ChatCommand.Ok;
        }

        // A commit/push of a finished thread has already run; its result is at the end of the thread's answer.
        var renderer = ChatRenderer.ForConsole(forcePlain: true);
        return Finish(api, renderer, await api.GetThreadAsync(threadId, CancellationToken.None), json, timedOut: false, null, false);
    }

    private static int Finish(ChatApiClient api, ChatRenderer renderer, ThreadDetailDto detail, bool json, bool timedOut, PendingApprovalDto? approval, bool question)
    {
        var url = api.ThreadUrl(detail.Summary.Id);
        if (json) AgentOutput.Write(ReportOf(detail, url, timedOut, approval, question));
        else if (timedOut) Console.WriteLine(Lang.T($"Thread masih berjalan ({detail.Summary.Status}). {url}", $"The thread is still running ({detail.Summary.Status}). {url}"));
        else if (approval is not null) renderer.Approval(approval, url);
        else renderer.Summary(detail, url);
        return ExitCodeOf(detail.Summary.Status, approval is not null || question, timedOut);
    }

    private static int Refuse(string message, int exitCode = ChatCommand.BadArguments)
    {
        Console.Error.WriteLine(message);
        return exitCode;
    }
}

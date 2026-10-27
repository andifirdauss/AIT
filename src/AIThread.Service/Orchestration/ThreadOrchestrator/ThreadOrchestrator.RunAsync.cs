using AIThread.Common.Localization;
using AIThread.Service.Approvals;
using AIThread.Service.Memory;
using AIThread.Service.Models;
using AIThread.Service.Testing;

namespace AIThread.Service.Orchestration;

public sealed partial class ThreadOrchestrator
{
    public async Task<ThreadResult> RunAsync(ThreadRequest request, IThreadObserver observer, CancellationToken cancellationToken)
    {
        // AIT's own output to the CLI follows the language the developer writes in, whatever the UI language.
        using var language = Lang.Use(TextLanguage.LangOfPrompt(request.Prompt, request.ConversationContext));

        // "1" / "2" / "3" right after a thread asked to commit/push answers that request; any other prompt drops it.
        var publish = publishApprovals.TakeAsk(request.SessionId, request.ProjectId);
        if (publish is not null && ApprovalReply.Parse(request.Prompt) is { } decision)
            return await PublishFromChatAsync(publish, decision, observer, cancellationToken);

        // "login <user> <password>" right after a tester asked for one runs that thread's test again with it.
        if (TestLogin.Parse(request.Prompt) is { } login && testLogins.TakeAsk(request.SessionId, request.ProjectId) is { } waiting)
            return await TestWithLoginAsync(waiting, login, request, observer, cancellationToken);

        // "password dev <value>" after a tester found the project's development password refused: kept for the project,
        // and that thread's test runs again with the test accounts.
        if (TestLogin.ParseDevPassword(request.Prompt) is { } devPassword && testLogins.TakeAsk(request.SessionId, request.ProjectId) is { } refused)
            return await TestWithDevPasswordAsync(refused, devPassword, request, observer, cancellationToken);

        // "jalankan test lanjutan <n>": a suggestion of the session's latest thread that has one with that number.
        if (TestFollowUpReply.Parse(request.Prompt) is { } number && await FollowUpThreadAsync(request, number, cancellationToken) is { } suggested)
            return await FollowUpFromChatAsync(suggested, number, request, observer, cancellationToken);

        // "buat skenario test lanjutan <n>" writes that suggestion's sheet into the project; "jalankan skenario <slug>"
        // reads it back and runs it.
        if (TestScenarioReply.ParseCreate(request.Prompt) is { } sheetOf && await FollowUpThreadAsync(request, sheetOf, cancellationToken) is { } source)
            return await ScenarioFromChatAsync(source, sheetOf, request, observer, cancellationToken);
        if (TestScenarioReply.ParseRun(request.Prompt) is { } run && await RunScenarioFromChatAsync(run.Slug, run.Agreed, request, observer, cancellationToken) is { } ran)
            return ran;

        // "ulangi" right after a thread of this chat left planned tasks unfinished runs them again from its plan.
        if (RetryReply.IsMatch(request.Prompt) && await RetryableSessionThreadAsync(request, cancellationToken) is { } retry)
            return await RetryFromChatAsync(retry, request, observer, cancellationToken);

        var prepared = await PrepareOrResumeAsync(Redacted(request), cancellationToken);
        return await ExecuteAsync(prepared, observer, cancellationToken);
    }
}

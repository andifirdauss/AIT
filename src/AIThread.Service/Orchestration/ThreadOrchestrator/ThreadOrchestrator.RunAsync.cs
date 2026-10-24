using AIThread.Service.Models;
using AIThread.Service.Testing;

namespace AIThread.Service.Orchestration;

public sealed partial class ThreadOrchestrator
{
    public async Task<ThreadResult> RunAsync(ThreadRequest request, IThreadObserver observer, CancellationToken cancellationToken)
    {
        // "login <user> <password>" right after a tester asked for one runs that thread's test again with it.
        if (TestLogin.Parse(request.Prompt) is { } login && testLogins.TakeAsk(request.SessionId, request.ProjectId) is { } waiting)
            return await TestWithLoginAsync(waiting, login, request, observer, cancellationToken);

        // "ulangi" right after a thread of this chat left planned tasks unfinished runs them again from its plan.
        if (RetryReply.IsMatch(request.Prompt) && await RetryableSessionThreadAsync(request, cancellationToken) is { } retry)
            return await RetryFromChatAsync(retry, request, observer, cancellationToken);

        var prepared = await PrepareOrResumeAsync(Redacted(request), cancellationToken);
        return await ExecuteAsync(prepared, observer, cancellationToken);
    }
}

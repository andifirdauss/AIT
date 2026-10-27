using AIThread.Service.Approvals;
using AIThread.Service.Models;

namespace AIThread.Service.Orchestration;

public sealed partial class ThreadOrchestrator
{
    public async Task<Guid> StartInBackgroundAsync(ThreadRequest request, CancellationToken cancellationToken)
    {
        // "1" / "2" / "3" (Telegram) right after a thread asked to commit/push: the result is added to that thread.
        if (publishApprovals.TakeAsk(request.SessionId, request.ProjectId) is { } publish && ApprovalReply.Parse(request.Prompt) is { } decision)
            return await PublishOnThreadAsync(publish, decision, cancellationToken);

        var prepared = await PrepareOrResumeAsync(Redacted(request), cancellationToken);
        _ = Task.Run(() => ExecuteAsync(prepared, NullThreadObserver.Instance, lifetime.ApplicationStopping), CancellationToken.None);
        return prepared.Thread.Id;
    }
}

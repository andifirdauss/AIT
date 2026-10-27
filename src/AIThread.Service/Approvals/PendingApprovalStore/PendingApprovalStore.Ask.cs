using AIThread.Service.Git;

namespace AIThread.Service.Approvals;

public sealed partial class PendingApprovalStore
{
    // A newer request of the same chat replaces the older one.
    public PendingPublish Ask(string sessionId, Guid projectId, Guid threadId, ApprovalRequest request, PublishJob job)
    {
        var pending = new PendingPublish(threadId, projectId, sessionId, request, job, timeProvider.GetUtcNow().UtcDateTime + Lifetime);
        _bySession[sessionId] = pending;
        return pending;
    }
}

using AIThread.Service.Approvals;
using AIThread.Service.Git;

namespace AIThread.Service.Interfaces;

// Commit/push requests of finished threads waiting for the developer's permission (in memory only).
public interface IPendingApprovalStore
{
    PendingPublish Ask(string sessionId, Guid projectId, Guid threadId, ApprovalRequest request, PublishJob job);

    PendingPublish? TakeAsk(string? sessionId, Guid projectId);

    PendingPublish? TakeThread(Guid threadId);

    PendingPublish? Find(Guid threadId);
}

namespace AIThread.Service.Approvals;

public sealed partial class PendingApprovalStore
{
    public PendingPublish? Find(Guid threadId) =>
        _bySession.Values.FirstOrDefault(p => p.ThreadId == threadId && p.ExpiresAt > timeProvider.GetUtcNow().UtcDateTime);
}

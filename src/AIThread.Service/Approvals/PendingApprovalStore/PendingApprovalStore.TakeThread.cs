namespace AIThread.Service.Approvals;

public sealed partial class PendingApprovalStore
{
    // The request of a thread, answered on its page.
    public PendingPublish? TakeThread(Guid threadId)
    {
        foreach (var (session, pending) in _bySession)
            if (pending.ThreadId == threadId && _bySession.TryRemove(new KeyValuePair<string, PendingPublish>(session, pending)))
                return pending.ExpiresAt > timeProvider.GetUtcNow().UtcDateTime ? pending : null;
        return null;
    }
}

namespace AIThread.Service.Approvals;

public sealed partial class PendingApprovalStore
{
    // The request this chat's reply answers; null when there is none for the project, or it expired.
    public PendingPublish? TakeAsk(string? sessionId, Guid projectId)
    {
        if (string.IsNullOrEmpty(sessionId) || !_bySession.TryGetValue(sessionId, out var pending) || pending.ProjectId != projectId) return null;
        if (!_bySession.TryRemove(new KeyValuePair<string, PendingPublish>(sessionId, pending))) return null;
        return pending.ExpiresAt > timeProvider.GetUtcNow().UtcDateTime ? pending : null;
    }
}

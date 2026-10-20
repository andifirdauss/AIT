namespace AIThread.Service.Orchestration;

public sealed partial class PendingQuestionStore
{
    // The question this prompt answers. A question of another project, or an expired one, is dropped and returned as
    // stale so the caller can close its thread.
    public PendingQuestion? TryTake(string? sessionId, Guid projectId, out PendingQuestion? stale)
    {
        stale = null;
        if (string.IsNullOrEmpty(sessionId) || !_bySession.TryRemove(sessionId, out var pending)) return null;
        if (pending.ProjectId == projectId && pending.ExpiresAt > timeProvider.GetUtcNow().UtcDateTime) return pending;

        stale = pending;
        return null;
    }
}

namespace AIThread.Service.Testing;

public sealed partial class TestLoginStore
{
    // The thread waiting for a login in this session and project, once; null when none (or it expired).
    public Guid? TakeAsk(string? sessionId, Guid projectId)
    {
        if (string.IsNullOrEmpty(sessionId) || !_asks.TryRemove((sessionId, projectId), out var ask)) return null;
        return ask.ExpiresAt > Now() ? ask.Value : null;
    }
}

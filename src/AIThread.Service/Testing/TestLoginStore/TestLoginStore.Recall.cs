namespace AIThread.Service.Testing;

public sealed partial class TestLoginStore
{
    // The login this session gave for the project, while it lasts.
    public TestLogin? Recall(string? sessionId, Guid projectId)
    {
        if (string.IsNullOrEmpty(sessionId) || !_logins.TryGetValue((sessionId, projectId), out var login)) return null;
        if (login.ExpiresAt > Now()) return login.Value;
        _logins.TryRemove(KeyValuePair.Create((sessionId, projectId), login));
        return null;
    }
}

namespace AIThread.Service.Testing;

public sealed partial class TestLoginStore
{
    // The tester of threadId asked for a login; the session's next "login <user> <password>" answers it.
    public void Ask(string sessionId, Guid projectId, Guid threadId) =>
        _asks[(sessionId, projectId)] = new Expiring<Guid>(threadId, Now() + AskLifetime);
}

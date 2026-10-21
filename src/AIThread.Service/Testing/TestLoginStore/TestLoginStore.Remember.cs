namespace AIThread.Service.Testing;

public sealed partial class TestLoginStore
{
    public void Remember(string sessionId, Guid projectId, TestLogin login) =>
        _logins[(sessionId, projectId)] = new Expiring<TestLogin>(login, Now() + LoginLifetime);
}

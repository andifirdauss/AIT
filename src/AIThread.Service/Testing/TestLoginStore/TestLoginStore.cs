using System.Collections.Concurrent;
using AIThread.Service.Interfaces;

namespace AIThread.Service.Testing;

// Logins given in the chat for testing, in memory only (gone after a restart): the thread whose tester is waiting for
// one, per session and project; the login a session gave, so its later threads use it without asking again; and every
// known password, masked out of the text that reaches the models (Redact).
public sealed partial class TestLoginStore(TimeProvider timeProvider) : ITestLoginStore
{
    public static readonly TimeSpan AskLifetime = TimeSpan.FromHours(2);
    public static readonly TimeSpan LoginLifetime = TimeSpan.FromHours(8);

    private readonly ConcurrentDictionary<(string Session, Guid Project), Expiring<Guid>> _asks = new();
    private readonly ConcurrentDictionary<(string Session, Guid Project), Expiring<TestLogin>> _logins = new();

    private sealed record Expiring<T>(T Value, DateTime ExpiresAt);
}

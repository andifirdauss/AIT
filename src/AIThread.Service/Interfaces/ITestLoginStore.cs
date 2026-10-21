using AIThread.Service.Testing;

namespace AIThread.Service.Interfaces;

public interface ITestLoginStore
{
    void Ask(string sessionId, Guid projectId, Guid threadId);

    Guid? TakeAsk(string? sessionId, Guid projectId);

    void Remember(string sessionId, Guid projectId, TestLogin login);

    TestLogin? Recall(string? sessionId, Guid projectId);

    string? Redact(string? text);
}

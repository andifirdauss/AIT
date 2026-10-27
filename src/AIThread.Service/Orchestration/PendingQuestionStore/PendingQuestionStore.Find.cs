namespace AIThread.Service.Orchestration;

public sealed partial class PendingQuestionStore
{
    public PendingQuestion? Find(Guid threadId) =>
        _bySession.Values.FirstOrDefault(p => p.ThreadId == threadId && p.ExpiresAt > timeProvider.GetUtcNow().UtcDateTime);
}

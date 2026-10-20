namespace AiThread.Service.Orchestration;

public sealed partial class PendingQuestionStore
{
    public PendingQuestion Add(Guid threadId, Guid projectId, string sessionId, PlannerQuestion question, PlannerConversation conversation)
    {
        var pending = new PendingQuestion(threadId, projectId, sessionId, question, conversation, timeProvider.GetUtcNow().UtcDateTime + Lifetime);
        _bySession[sessionId] = pending;
        return pending;
    }
}

using AIThread.Service.Orchestration;

namespace AIThread.Service.Interfaces;

public interface IPendingQuestionStore
{
    PendingQuestion Add(Guid threadId, Guid projectId, string sessionId, PlannerQuestion question, PlannerConversation conversation);

    PendingQuestion? TryTake(string? sessionId, Guid projectId, out PendingQuestion? stale);

    bool Remove(Guid threadId);

    IReadOnlyList<PendingQuestion> TakeExpired();
}

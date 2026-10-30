using AIThread.Common.DTOs;

namespace AIThread.Service.Orchestration;

public sealed partial class ThreadOrchestrator
{
    // A permission request of the thread was answered; next is the thread that goes on (the same one after a
    // commit/push, the resumed planner otherwise).
    private void ApprovalAnswered(Guid threadId, Guid projectId, Guid next) =>
        eventBus.Publish(AppEvent.Create(AppEventTypes.ApprovalChanged, new { ThreadId = threadId, ProjectId = projectId, NextThreadId = next }, threadId));
}

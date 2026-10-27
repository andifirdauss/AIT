using AIThread.Common.DTOs;
using AIThread.Common.Enums;
using AIThread.Service.Approvals;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Orchestration;

public sealed partial class ThreadOrchestrator
{
    // A commit/push answered outside the chat it was asked in (the thread page, Telegram): runs it and adds the result
    // to the thread's answer.
    private async Task<Guid> PublishOnThreadAsync(PendingPublish pending, ApprovalDecision decision, CancellationToken cancellationToken)
    {
        var text = await AnswerPublishAsync(pending, decision, cancellationToken);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var thread = await db.Threads.FirstOrDefaultAsync(t => t.Id == pending.ThreadId, cancellationToken);
        if (thread is null) return pending.ThreadId;

        thread.FinalResponse = string.IsNullOrWhiteSpace(thread.FinalResponse) ? text : $"{thread.FinalResponse}\n\n{text}";
        thread.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        eventBus.Publish(AppEvent.Create(AppEventTypes.ThreadUpdated, new { Id = thread.Id, thread.Status }, thread.Id));
        return thread.Id;
    }
}

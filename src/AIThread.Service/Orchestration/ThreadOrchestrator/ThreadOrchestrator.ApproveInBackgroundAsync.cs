using AIThread.Common.Enums;
using AIThread.Common.Localization;
using AIThread.Service.Models;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Orchestration;

public sealed partial class ThreadOrchestrator
{
    // The thread page's answer to a permission request. A commit/push of a finished thread runs here and its result is
    // added to the thread's answer; a paused planner continues in the background as if the developer had replied in
    // the chat it came from.
    public async Task<Guid> ApproveInBackgroundAsync(Guid threadId, ApprovalDecision decision, CancellationToken cancellationToken)
    {
        if (publishApprovals.TakeThread(threadId) is { } publish)
            return await PublishOnThreadAsync(publish, decision, cancellationToken);

        if (questions.Find(threadId) is not { Question.Approval: not null } pending)
            throw new AppValidationException(Lang.T("Thread ini tidak sedang menunggu izin.", "This thread is not waiting for permission."));

        ThreadSource source;
        await using (var db = await dbFactory.CreateDbContextAsync(cancellationToken))
            source = await db.Threads.Where(t => t.Id == threadId).Select(t => t.Source).FirstOrDefaultAsync(cancellationToken);

        var reply = decision switch { ApprovalDecision.Once => "1", ApprovalDecision.Always => "2", ApprovalDecision.AlwaysPattern => "4", ApprovalDecision.AlwaysAll => "5", _ => "3" };
        return await StartInBackgroundAsync(new ThreadRequest(source, reply, null, null, null, pending.ProjectId, pending.SessionId), cancellationToken);
    }
}

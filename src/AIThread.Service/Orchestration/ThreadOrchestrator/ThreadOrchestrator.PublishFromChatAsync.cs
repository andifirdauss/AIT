using AIThread.Common.Enums;
using AIThread.Service.Approvals;
using AIThread.Service.Models;

namespace AIThread.Service.Orchestration;

public sealed partial class ThreadOrchestrator
{
    // "1", "2" or "3" in the chat right after a thread asked to commit/push.
    private async Task<ThreadResult> PublishFromChatAsync(PendingPublish pending, ApprovalDecision decision, IThreadObserver observer, CancellationToken cancellationToken)
    {
        var settings = await settingService.GetRuntimeAsync(cancellationToken);
        var progress = new ClientProgress(observer, settings.StreamProgressToClient);
        await progress.FinalAsync(await AnswerPublishAsync(pending, decision, cancellationToken), cancellationToken);
        return new ThreadResult(pending.ThreadId, ThreadStatus.Completed, progress.Transcript, 0);
    }
}

using AIThread.Common.Localization;
using AIThread.Service.Git;
using AIThread.Service.Interfaces;

namespace AIThread.Service.Orchestration;

public sealed partial class ThreadOrchestrator
{
    // Commits and pushes, then lets git tracking see the new commit; returns the lines for the answer.
    private async Task<string> RunPublishAsync(PublishJob job, Guid projectId, Guid threadId, CancellationToken cancellationToken)
    {
        var outcome = await gitPublisher.RunAsync(job, cancellationToken);
        var summary = string.Join(" · ", outcome.Lines);
        if (outcome.Succeeded) activityLog.Success("thread", summary, threadId, projectId);
        else activityLog.Warn("thread", summary, threadId, projectId);
        gitChanges.ThreadChanged(projectId, threadId);
        return Lang.T("\n\n📤 **Commit/push**\n", "\n\n📤 **Commit/push**\n") + string.Join("\n", outcome.Lines.Select(l => "- " + l));
    }
}

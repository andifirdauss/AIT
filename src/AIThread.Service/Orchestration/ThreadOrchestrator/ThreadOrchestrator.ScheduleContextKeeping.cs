using AIThread.Common.Entities;
using AIThread.Common.Enums;
using AIThread.Service.Models;

namespace AIThread.Service.Orchestration;

public sealed partial class ThreadOrchestrator
{
    // Only Claude Code chats carry thread anchors, so only their next turn can pick the pack up.
    private void ScheduleContextKeeping(ThreadRun thread, ThreadRequest request)
    {
        if (request.Source != ThreadSource.ClaudeCode) return;
        _ = Task.Run(() => contextKeeper.UpdateAsync(thread, request, lifetime.ApplicationStopping), CancellationToken.None);
    }
}

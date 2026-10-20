using AiThread.Common.Entities;
using AiThread.Common.Enums;
using AiThread.Service.Models;

namespace AiThread.Service.Orchestration;

public sealed partial class ThreadOrchestrator
{
    // Only Claude Code chats carry thread anchors, so only their next turn can pick the pack up.
    private void ScheduleContextKeeping(ThreadRun thread, ThreadRequest request)
    {
        if (request.Source != ThreadSource.ClaudeCode) return;
        _ = Task.Run(() => contextKeeper.UpdateAsync(thread, request, lifetime.ApplicationStopping), CancellationToken.None);
    }
}

using AIThread.Common.Chat;
using AIThread.Common.Entities;
using AIThread.Common.Enums;
using AIThread.Service.Models;

namespace AIThread.Service.Orchestration;

public sealed partial class ThreadOrchestrator
{
    // Only CLI chats (through their thread anchors) and aithread chat sessions (through their SessionId) can pick the
    // pack up on their next turn; a dashboard prompt stands on its own.
    private void ScheduleContextKeeping(ThreadRun thread, ThreadRequest request)
    {
        if (request.Source != ThreadSource.ClaudeCode && !ChatSessionKey.IsValid(request.SessionId)) return;
        _ = Task.Run(() => contextKeeper.UpdateAsync(thread, request, lifetime.ApplicationStopping), CancellationToken.None);
    }
}

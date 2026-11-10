using AIThread.Common.DTOs;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.Orchestration;

public sealed partial class ThreadOrchestrator
{
    // The answer has usually gone by now: the dashboard and the tray tell the developer how the extraction ended, unless
    // silent mode is on. A notification that cannot go out must not touch the memory that was saved.
    private async Task NotifyMemoryAsync(MemoryExtraction result, Guid threadId)
    {
        if (result.Saved == 0 && !result.Failed) return;
        try
        {
            if ((await settingService.GetRuntimeAsync(lifetime.ApplicationStopping)).SilentNotifications) return;
            eventBus.Publish(AppEvent.Create(AppEventTypes.MemoryExtracted, new MemoryExtractedDto(result.Saved, result.Failed), threadId));
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Memory notification of thread {ThreadId} not sent", threadId);
        }
    }
}

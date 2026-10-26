using AIThread.Common.DTOs;

namespace AIThread.Service.Memory;

public sealed partial class ModuleSummaryWriter
{
    // The Memory menu reloads its banners on memory events.
    private void PublishStatus() =>
        eventBus.Publish(AppEvent.Create(AppEventTypes.MemoryChanged, new { id = (Guid?)null, action = "changelog" }));
}

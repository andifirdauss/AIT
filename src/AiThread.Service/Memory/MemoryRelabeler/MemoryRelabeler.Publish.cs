using AiThread.Common.DTOs;

namespace AiThread.Service.Memory;

public sealed partial class MemoryRelabeler
{
    private void Publish(string action) =>
        eventBus.Publish(AppEvent.Create(AppEventTypes.MemoryChanged, new { id = (Guid?)null, action }));
}

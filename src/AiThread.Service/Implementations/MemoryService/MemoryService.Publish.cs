using AiThread.Common.DTOs;

namespace AiThread.Service.Implementations;

public sealed partial class MemoryService
{
    private void Publish(string action, Guid? id) =>
        eventBus.Publish(AppEvent.Create(AppEventTypes.MemoryChanged, new { id, action }));
}

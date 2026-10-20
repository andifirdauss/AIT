using AIThread.Common.DTOs;

namespace AIThread.Service.Memory;

public sealed partial class ModuleMerger
{
    private void Publish(string action, string key) =>
        eventBus.Publish(AppEvent.Create(AppEventTypes.MemoryChanged, new { id = (Guid?)null, action, projectKey = key }));
}

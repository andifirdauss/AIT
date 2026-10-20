using AIThread.Common.DTOs;
using AIThread.Common.Entities;

namespace AIThread.Service.Implementations;

public sealed partial class BusinessMemoryService
{
    private void Publish(string action, BusinessPage page) =>
        eventBus.Publish(AppEvent.Create(AppEventTypes.BusinessChanged, new { action, id = page.Id, projectKey = page.ProjectKey }));
}

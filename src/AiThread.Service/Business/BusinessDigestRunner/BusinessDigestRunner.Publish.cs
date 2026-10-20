using AiThread.Common.DTOs;

namespace AiThread.Service.Business;

public sealed partial class BusinessDigestRunner
{
    private void Publish(string action, string projectKey) =>
        eventBus.Publish(AppEvent.Create(AppEventTypes.BusinessChanged, new { action, projectKey }));
}

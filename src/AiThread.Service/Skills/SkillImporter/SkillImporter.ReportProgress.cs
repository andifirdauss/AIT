using AiThread.Common.DTOs;
using AiThread.Common.Enums;

namespace AiThread.Service.Skills;

public sealed partial class SkillImporter
{
    private void ReportProgress(Guid sourceId, string text)
    {
        progress.Set(sourceId, text);
        eventBus.Publish(AppEvent.Create(AppEventTypes.SkillChanged, new { sourceId, status = SkillSourceStatus.Analyzing.ToString(), progress = text }));
    }
}

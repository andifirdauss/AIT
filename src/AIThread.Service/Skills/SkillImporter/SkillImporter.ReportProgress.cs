using AIThread.Common.DTOs;
using AIThread.Common.Enums;

namespace AIThread.Service.Skills;

public sealed partial class SkillImporter
{
    private void ReportProgress(Guid sourceId, string text)
    {
        progress.Set(sourceId, text);
        eventBus.Publish(AppEvent.Create(AppEventTypes.SkillChanged, new { sourceId, status = SkillSourceStatus.Analyzing.ToString(), progress = text }));
    }
}

using AIThread.Common.DTOs;
using AIThread.Service.Interfaces;

namespace AIThread.Service.Implementations;

public sealed partial class SkillService
{
    public async Task<SkillSettingsDto> UpdateSettingsAsync(UpdateSkillSettingsRequest request, CancellationToken cancellationToken = default)
    {
        var settings = await settingService.UpdateSkillsAsync(request, cancellationToken);
        activityLog.Info("skill", $"Setting Skill Orchestrator disimpan (model: {settings.SkillOrchestratorModel ?? "otomatis — node Planner"}).");
        return new SkillSettingsDto(settings.SkillOrchestratorModel, settings.SkillPromptMaxChars, paths.Root);
    }
}

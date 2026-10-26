using AIThread.Common.DTOs;
using AIThread.Common.Localization;
using AIThread.Service.Interfaces;

namespace AIThread.Service.Implementations;

public sealed partial class SkillService
{
    public async Task<SkillSettingsDto> UpdateSettingsAsync(UpdateSkillSettingsRequest request, CancellationToken cancellationToken = default)
    {
        var settings = await settingService.UpdateSkillsAsync(request, cancellationToken);
        activityLog.Info("skill", Lang.T($"Pengaturan Skill Orchestrator disimpan (model: {settings.SkillOrchestratorModel ?? "otomatis — node Planner"}).", $"Skill Orchestrator settings saved (model: {settings.SkillOrchestratorModel ?? "automatic — Planner node"})."));
        return new SkillSettingsDto(settings.SkillOrchestratorModel, settings.SkillPromptMaxChars, paths.Root);
    }
}

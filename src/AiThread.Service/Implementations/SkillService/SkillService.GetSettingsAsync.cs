using AiThread.Common.DTOs;

namespace AiThread.Service.Implementations;

public sealed partial class SkillService
{
    public async Task<SkillSettingsDto> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        var settings = await settingService.GetRuntimeAsync(cancellationToken);
        return new SkillSettingsDto(settings.SkillOrchestratorModel, settings.SkillPromptMaxChars, paths.Root);
    }
}

using AIThread.Common.Enums;
using AIThread.Service.Models;

namespace AIThread.Service.Skills;

public sealed partial class SkillAi
{
    public async Task<string> ResolveModelAsync(CancellationToken cancellationToken)
    {
        var settings = await settingService.GetRuntimeAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(settings.SkillOrchestratorModel)) return settings.SkillOrchestratorModel;
        return (await nodeService.GetActiveAsync(NodeRole.Planner, cancellationToken)).FirstOrDefault()?.Model
               ?? throw new AppValidationException("Pilih model Skill Orchestrator di Setting → Skill (atau tambahkan node Planner aktif).");
    }
}

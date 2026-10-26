using AIThread.Common.Localization;
using AIThread.Service.Models;

namespace AIThread.Service.Implementations;

public sealed partial class TestScenarioService
{
    // The project with its key and the developer name for a change.
    private async Task<(ProjectContext Project, string By)> ProjectAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var project = await projectService.GetContextAsync(projectId, cancellationToken) ?? throw new AppValidationException(Lang.T("Project tidak ditemukan.", "Project not found."));
        if (string.IsNullOrEmpty(project.Key)) throw new AppValidationException(Lang.T("Project ini belum punya project key, jadi skenario test belum bisa disimpan.", "This project has no project key yet, so test scenarios cannot be saved yet."));
        return (project, (await settingService.GetRuntimeAsync(cancellationToken)).EffectiveDeveloperName);
    }
}

using AIThread.Service.Models;

namespace AIThread.Service.Implementations;

public sealed partial class TestScenarioService
{
    // The project with its key and the developer name for a change.
    private async Task<(ProjectContext Project, string By)> ProjectAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var project = await projectService.GetContextAsync(projectId, cancellationToken) ?? throw new AppValidationException("Project tidak ditemukan.");
        if (string.IsNullOrEmpty(project.Key)) throw new AppValidationException("Project ini belum punya project key, jadi skenario test belum bisa disimpan.");
        return (project, (await settingService.GetRuntimeAsync(cancellationToken)).EffectiveDeveloperName);
    }
}

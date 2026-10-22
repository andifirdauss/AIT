using AIThread.Service.Models;

namespace AIThread.Service.Implementations;

public sealed partial class UnitTestService
{
    private async Task<ProjectContext> ContextAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var project = await projectService.GetContextAsync(projectId, cancellationToken) ?? throw new AppValidationException("Project tidak ditemukan.");
        if (string.IsNullOrEmpty(project.Key)) throw new AppValidationException("Project ini belum punya project key.");
        return project;
    }
}

using AIThread.Service.Models;

namespace AIThread.Service.Implementations;

public sealed partial class MemoryCatalogService
{
    private async Task<ProjectContext> ContextAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var project = await projectService.GetContextAsync(projectId, cancellationToken) ?? throw new AppValidationException("Project tidak ditemukan.");
        if (string.IsNullOrEmpty(project.Key)) throw new AppValidationException("Project belum punya key; buka lagi menu Project sebentar lagi.");
        return project;
    }
}

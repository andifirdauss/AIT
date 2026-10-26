using AIThread.Common.Localization;
using AIThread.Service.Models;

namespace AIThread.Service.Implementations;

public sealed partial class MemoryCatalogService
{
    private async Task<ProjectContext> ContextAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var project = await projectService.GetContextAsync(projectId, cancellationToken) ?? throw new AppValidationException(Lang.T("Project tidak ditemukan.", "Project not found."));
        if (string.IsNullOrEmpty(project.Key)) throw new AppValidationException(Lang.T("Project belum punya key; buka lagi menu Project sebentar lagi.", "The project has no key yet; open the Project menu again in a moment."));
        return project;
    }
}

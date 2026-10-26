using AIThread.Common.Localization;
using AIThread.Service.Models;

namespace AIThread.Service.Implementations;

public sealed partial class UnitTestService
{
    private async Task<ProjectContext> ContextAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var project = await projectService.GetContextAsync(projectId, cancellationToken) ?? throw new AppValidationException(Lang.T("Project tidak ditemukan.", "Project not found."));
        if (string.IsNullOrEmpty(project.Key)) throw new AppValidationException(Lang.T("Project ini belum punya project key.", "This project has no project key yet."));
        return project;
    }
}

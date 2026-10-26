using AIThread.Common.Localization;
using AIThread.Service.Models;

namespace AIThread.Service.Implementations;

public sealed partial class TestAccountService
{
    // The project and the developer name for a change; a project without a key cannot keep accounts.
    private async Task<(ProjectContext Project, string By)> ContextAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var project = await projectService.GetContextAsync(projectId, cancellationToken) ?? throw new AppValidationException(Lang.T("Project tidak ditemukan.", "Project not found."));
        if (string.IsNullOrEmpty(project.Key)) throw new AppValidationException(NoKey);
        var by = (await settingService.GetRuntimeAsync(cancellationToken)).EffectiveDeveloperName;
        return (project, by);
    }

    private static string NoKey => Lang.T("Project ini belum punya project key, jadi akun uji belum bisa disimpan (akun uji dibagi ke tim lewat project key).", "This project has no project key yet, so test accounts cannot be saved yet (test accounts are shared with the team through the project key).");
}

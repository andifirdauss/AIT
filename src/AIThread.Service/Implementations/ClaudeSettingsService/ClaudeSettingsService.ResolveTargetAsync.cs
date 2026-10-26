using AIThread.Common.Enums;
using AIThread.Common.Localization;
using AIThread.Service.Models;

namespace AIThread.Service.Implementations;

public sealed partial class ClaudeSettingsService
{
    private async Task<(string Path, string? ProjectRoot)> ResolveTargetAsync(ClaudeSettingsScope scope, Guid? projectId, CancellationToken cancellationToken)
    {
        if (scope == ClaudeSettingsScope.User)
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return (Path.Combine(home, ".claude", "settings.json"), null);
        }

        if (projectId is null) throw new AppValidationException(Lang.T("Pilih project terlebih dahulu.", "Choose a project first."));
        var project = await projectService.GetContextAsync(projectId.Value, cancellationToken)
                      ?? throw new AppValidationException(Lang.T("Project tidak ditemukan.", "Project not found."));
        return (ProjectSettingsPath(project.RootPath), project.RootPath);
    }
}

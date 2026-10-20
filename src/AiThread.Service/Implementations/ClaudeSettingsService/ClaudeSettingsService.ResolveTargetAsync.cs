using AiThread.Common.Enums;
using AiThread.Service.Models;

namespace AiThread.Service.Implementations;

public sealed partial class ClaudeSettingsService
{
    private async Task<(string Path, string? ProjectRoot)> ResolveTargetAsync(ClaudeSettingsScope scope, Guid? projectId, CancellationToken cancellationToken)
    {
        if (scope == ClaudeSettingsScope.User)
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return (Path.Combine(home, ".claude", "settings.json"), null);
        }

        if (projectId is null) throw new AppValidationException("Pilih project terlebih dahulu.");
        var project = await projectService.GetContextAsync(projectId.Value, cancellationToken)
                      ?? throw new AppValidationException("Project tidak ditemukan.");
        return (ProjectSettingsPath(project.RootPath), project.RootPath);
    }
}

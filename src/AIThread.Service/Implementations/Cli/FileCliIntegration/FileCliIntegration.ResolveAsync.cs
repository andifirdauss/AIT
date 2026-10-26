using AIThread.Common.Enums;
using AIThread.Common.Localization;
using AIThread.Service.Models;

namespace AIThread.Service.Implementations.Cli;

public abstract partial class FileCliIntegration
{
    private async Task<(string Path, string? ProjectRoot)> ResolveAsync(CliScope scope, Guid? projectId, CancellationToken cancellationToken)
    {
        if (scope == CliScope.Global) return (Info.GlobalPath, null);

        if (Info.ProjectFile is null || projectService is null)
            throw new AppValidationException(Lang.T($"{Info.Name} hanya punya konfigurasi global (per pengguna).", $"{Info.Name} only has a global configuration (per user)."));
        if (projectId is null) throw new AppValidationException(Lang.T("Pilih project terlebih dahulu.", "Choose a project first."));
        var project = await projectService.GetContextAsync(projectId.Value, cancellationToken)
                      ?? throw new AppValidationException(Lang.T("Project tidak ditemukan.", "Project not found."));
        return (Path.Combine(project.RootPath, Info.ProjectFile), project.RootPath);
    }
}

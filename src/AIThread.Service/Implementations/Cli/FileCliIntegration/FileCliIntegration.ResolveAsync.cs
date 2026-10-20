using AIThread.Common.Enums;
using AIThread.Service.Models;

namespace AIThread.Service.Implementations.Cli;

public abstract partial class FileCliIntegration
{
    private async Task<(string Path, string? ProjectRoot)> ResolveAsync(CliScope scope, Guid? projectId, CancellationToken cancellationToken)
    {
        if (scope == CliScope.Global) return (Info.GlobalPath, null);

        if (Info.ProjectFile is null || projectService is null)
            throw new AppValidationException($"{Info.Name} hanya punya konfigurasi global (per pengguna).");
        if (projectId is null) throw new AppValidationException("Pilih project terlebih dahulu.");
        var project = await projectService.GetContextAsync(projectId.Value, cancellationToken)
                      ?? throw new AppValidationException("Project tidak ditemukan.");
        return (Path.Combine(project.RootPath, Info.ProjectFile), project.RootPath);
    }
}

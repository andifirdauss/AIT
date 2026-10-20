using AIThread.Common.DTOs;
using AIThread.Service.Models;

namespace AIThread.Service.Implementations.Cli;

public abstract partial class FileCliIntegration
{
    public async Task<IReadOnlyList<ProjectCliStatusDto>> GetProjectStatusesAsync(string proxyBaseUrl, CancellationToken cancellationToken = default)
    {
        if (Info.ProjectFile is null || projectService is null) return [];

        var expected = ConfigBaseUrl(proxyBaseUrl);
        var statuses = new List<ProjectCliStatusDto>();
        foreach (var project in await projectService.GetAllAsync(cancellationToken))
        {
            var path = Path.Combine(project.RootPath, Info.ProjectFile);
            try
            {
                var current = CurrentBaseUrl(await ReadAsync(path, cancellationToken));
                var enabled = SameUrl(current, expected);
                statuses.Add(new ProjectCliStatusDto(project.Id, enabled, enabled ? null : current, path, null));
            }
            catch (Exception ex) when (ex is AppValidationException or IOException or UnauthorizedAccessException)
            {
                statuses.Add(new ProjectCliStatusDto(project.Id, false, null, path, ex.Message));
            }
        }
        return statuses;
    }
}

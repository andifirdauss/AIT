using AiThread.Common.DTOs;
using AiThread.Service.Models;

namespace AiThread.Service.Implementations;

public sealed partial class ClaudeSettingsService
{
    public async Task<IReadOnlyList<ProjectClaudeIntegrationDto>> GetProjectStatusesAsync(string proxyBaseUrl, CancellationToken cancellationToken = default)
    {
        var statuses = new List<ProjectClaudeIntegrationDto>();
        foreach (var project in await projectService.GetAllAsync(cancellationToken))
        {
            var path = ProjectSettingsPath(project.RootPath);
            try
            {
                var baseUrl = GetString((await ReadAsync(path, cancellationToken))["env"]?["ANTHROPIC_BASE_URL"]);
                var enabled = baseUrl is not null && SameUrl(baseUrl, proxyBaseUrl);
                statuses.Add(new ProjectClaudeIntegrationDto(project.Id, enabled, enabled ? null : baseUrl, path, null));
            }
            catch (Exception ex) when (ex is AppValidationException or IOException or UnauthorizedAccessException)
            {
                statuses.Add(new ProjectClaudeIntegrationDto(project.Id, false, null, path, ex.Message));
            }
        }
        return statuses;
    }
}

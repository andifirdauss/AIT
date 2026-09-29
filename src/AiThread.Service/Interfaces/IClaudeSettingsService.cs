using AiThread.Common.DTOs;
using AiThread.Common.Enums;

namespace AiThread.Service.Interfaces;

// Points Claude Code at this proxy through the "env" block of its settings.json.
public interface IClaudeSettingsService
{
    Task<ClaudeSettingsStatusDto> GetStatusAsync(ClaudeSettingsScope scope, Guid? projectId, string proxyBaseUrl, CancellationToken cancellationToken = default);

    Task<ClaudeSettingsStatusDto> ApplyAsync(ClaudeSettingsApplyRequest request, CancellationToken cancellationToken = default);

    Task<ClaudeSettingsStatusDto> RevertAsync(ClaudeSettingsScope scope, Guid? projectId, string proxyBaseUrl, CancellationToken cancellationToken = default);

    // Project-scope status (.claude/settings.local.json) of every registered project, for the project list toggle.
    Task<IReadOnlyList<ProjectClaudeIntegrationDto>> GetProjectStatusesAsync(string proxyBaseUrl, CancellationToken cancellationToken = default);
}

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

    // The /clear hooks (SessionEnd + SessionStart) of the context keeper; independent from the proxy env block.
    Task<ClaudeHooksStatusDto> GetHooksStatusAsync(ClaudeSettingsScope scope, Guid? projectId, string baseUrl, CancellationToken cancellationToken = default);

    Task<ClaudeHooksStatusDto> ApplyHooksAsync(ClaudeSettingsApplyRequest request, CancellationToken cancellationToken = default);

    Task<ClaudeHooksStatusDto> RevertHooksAsync(ClaudeSettingsScope scope, Guid? projectId, string baseUrl, CancellationToken cancellationToken = default);
}

using AiThread.Common.DTOs;

namespace AiThread.Service.Implementations.Cli;

public sealed partial class ClaudeCodeIntegration
{
    public async Task<IReadOnlyList<ProjectCliStatusDto>> GetProjectStatusesAsync(string proxyBaseUrl, CancellationToken cancellationToken = default) =>
        (await claude.GetProjectStatusesAsync(proxyBaseUrl, cancellationToken))
        .Select(s => new ProjectCliStatusDto(s.ProjectId, s.Enabled, s.CurrentBaseUrl, s.Path, s.Error))
        .ToList();
}

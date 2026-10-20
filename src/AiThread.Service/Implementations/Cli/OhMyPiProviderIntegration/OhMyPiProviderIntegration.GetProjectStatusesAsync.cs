using AiThread.Common.DTOs;

namespace AiThread.Service.Implementations.Cli;

public sealed partial class OhMyPiProviderIntegration
{
    // models.yml has no per-project counterpart.
    public Task<IReadOnlyList<ProjectCliStatusDto>> GetProjectStatusesAsync(string proxyBaseUrl, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ProjectCliStatusDto>>([]);
}

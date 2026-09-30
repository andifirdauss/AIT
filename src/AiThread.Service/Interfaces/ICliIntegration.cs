using AiThread.Common.DTOs;
using AiThread.Common.Enums;

namespace AiThread.Service.Interfaces;

// One coding CLI that can be pointed at this proxy (Claude Code, oh-my-pi, …). Applying writes the CLI's own
// configuration; reverting removes only what AiThread wrote.
public interface ICliIntegration
{
    CliInfoDto Info { get; }

    Task<CliStatusDto> GetStatusAsync(CliScope scope, Guid? projectId, string proxyBaseUrl, CancellationToken cancellationToken = default);

    Task<CliStatusDto> ApplyAsync(CliScope scope, Guid? projectId, string proxyBaseUrl, CancellationToken cancellationToken = default);

    Task<CliStatusDto> RevertAsync(CliScope scope, Guid? projectId, string proxyBaseUrl, CancellationToken cancellationToken = default);

    // Project-scope status of every registered project, for the per-project toggles.
    Task<IReadOnlyList<ProjectCliStatusDto>> GetProjectStatusesAsync(string proxyBaseUrl, CancellationToken cancellationToken = default);
}

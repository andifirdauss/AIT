using AIThread.Common.DTOs;
using AIThread.Common.Enums;

namespace AIThread.Service.Implementations.Cli;

public sealed partial class ClaudeCodeIntegration
{
    public async Task<CliStatusDto> RevertAsync(CliScope scope, Guid? projectId, string proxyBaseUrl, CancellationToken cancellationToken = default) =>
        ToStatus(scope, await claude.RevertAsync(ToClaudeScope(scope), projectId, proxyBaseUrl, cancellationToken));
}

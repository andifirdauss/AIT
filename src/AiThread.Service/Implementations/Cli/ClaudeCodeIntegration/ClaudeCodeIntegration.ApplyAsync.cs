using AiThread.Common.DTOs;
using AiThread.Common.Enums;

namespace AiThread.Service.Implementations.Cli;

public sealed partial class ClaudeCodeIntegration
{
    public async Task<CliStatusDto> ApplyAsync(CliScope scope, Guid? projectId, string proxyBaseUrl, CancellationToken cancellationToken = default) =>
        ToStatus(scope, await claude.ApplyAsync(new ClaudeSettingsApplyRequest(ToClaudeScope(scope), projectId, proxyBaseUrl, null), cancellationToken));
}

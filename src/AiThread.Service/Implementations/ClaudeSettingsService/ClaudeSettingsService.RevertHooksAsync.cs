using AiThread.Common.DTOs;
using AiThread.Common.Enums;

namespace AiThread.Service.Implementations;

public sealed partial class ClaudeSettingsService
{
    public async Task<ClaudeHooksStatusDto> RevertHooksAsync(ClaudeSettingsScope scope, Guid? projectId, string baseUrl, CancellationToken cancellationToken = default)
    {
        var target = await ResolveTargetAsync(scope, projectId, cancellationToken);
        if (File.Exists(target.Path))
        {
            var root = await ReadAsync(target.Path, cancellationToken);
            ClaudeHookConfig.Remove(root);
            await WriteAsync(target.Path, root, cancellationToken);
        }
        return await GetHooksStatusAsync(scope, projectId, baseUrl, cancellationToken);
    }
}

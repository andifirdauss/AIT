using System.Text.Json.Nodes;
using AiThread.Common.DTOs;
using AiThread.Common.Enums;

namespace AiThread.Service.Implementations;

public sealed partial class ClaudeSettingsService
{
    public async Task<ClaudeHooksStatusDto> GetHooksStatusAsync(ClaudeSettingsScope scope, Guid? projectId, string baseUrl, CancellationToken cancellationToken = default)
    {
        var target = await ResolveTargetAsync(scope, projectId, cancellationToken);
        var installed = ClaudeHookConfig.InstalledBaseUrl(await ReadAsync(target.Path, cancellationToken));
        return new ClaudeHooksStatusDto(
            scope,
            target.Path,
            installed is not null && SameUrl(installed, baseUrl),
            installed,
            new JsonObject { ["hooks"] = ClaudeHookConfig.Block(baseUrl) }.ToJsonString(WriteOptions));
    }
}

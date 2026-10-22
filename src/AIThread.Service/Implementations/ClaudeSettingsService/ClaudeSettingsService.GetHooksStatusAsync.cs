using System.Text.Json.Nodes;
using AIThread.Common.DTOs;
using AIThread.Common.Enums;

namespace AIThread.Service.Implementations;

public sealed partial class ClaudeSettingsService
{
    public async Task<ClaudeHooksStatusDto> GetHooksStatusAsync(ClaudeSettingsScope scope, Guid? projectId, string baseUrl, CancellationToken cancellationToken = default)
    {
        var target = await ResolveTargetAsync(scope, projectId, cancellationToken);
        var root = await ReadAsync(target.Path, cancellationToken);
        var installed = ClaudeHookConfig.InstalledBaseUrl(root);
        return new ClaudeHooksStatusDto(
            scope,
            target.Path,
            installed is not null && SameUrl(installed, baseUrl) && ClaudeHookConfig.IsComplete(root),
            installed,
            new JsonObject { ["hooks"] = ClaudeHookConfig.Block(baseUrl) }.ToJsonString(WriteOptions));
    }
}

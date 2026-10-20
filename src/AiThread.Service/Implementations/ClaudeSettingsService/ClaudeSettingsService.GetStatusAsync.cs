using System.Text.Json.Nodes;
using AiThread.Common.DTOs;
using AiThread.Common.Enums;

namespace AiThread.Service.Implementations;

public sealed partial class ClaudeSettingsService
{
    public async Task<ClaudeSettingsStatusDto> GetStatusAsync(ClaudeSettingsScope scope, Guid? projectId, string proxyBaseUrl, CancellationToken cancellationToken = default)
    {
        var target = await ResolveTargetAsync(scope, projectId, cancellationToken);
        var root = await ReadAsync(target.Path, cancellationToken);
        var currentBaseUrl = GetString(root["env"]?["ANTHROPIC_BASE_URL"]);
        var env = await BuildEnvAsync(proxyBaseUrl, null, target.ProjectRoot, cancellationToken);

        return new ClaudeSettingsStatusDto(
            scope,
            target.Path,
            File.Exists(target.Path),
            currentBaseUrl is not null && SameUrl(currentBaseUrl, proxyBaseUrl),
            currentBaseUrl,
            new JsonObject { ["env"] = env }.ToJsonString(WriteOptions));
    }
}

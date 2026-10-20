using System.Text.Json.Nodes;
using AiThread.Common.DTOs;
using AiThread.Common.Enums;

namespace AiThread.Service.Implementations;

public sealed partial class ClaudeSettingsService
{
    public async Task<ClaudeSettingsStatusDto> RevertAsync(ClaudeSettingsScope scope, Guid? projectId, string proxyBaseUrl, CancellationToken cancellationToken = default)
    {
        var target = await ResolveTargetAsync(scope, projectId, cancellationToken);
        if (File.Exists(target.Path))
        {
            var root = await ReadAsync(target.Path, cancellationToken);
            if (root["env"] is JsonObject env)
            {
                foreach (var key in ManagedKeys) env.Remove(key);
                if (GetString(env["ANTHROPIC_CUSTOM_HEADERS"]) is { } headers && IsAiThreadHeader(headers))
                    env.Remove("ANTHROPIC_CUSTOM_HEADERS");
                if (env.Count == 0) root.Remove("env");
            }
            await WriteAsync(target.Path, root, cancellationToken);
        }

        return await GetStatusAsync(scope, projectId, proxyBaseUrl, cancellationToken);
    }
}

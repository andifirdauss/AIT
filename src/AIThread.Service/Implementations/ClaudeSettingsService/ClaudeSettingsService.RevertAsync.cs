using System.Text.Json.Nodes;
using AIThread.Common.DTOs;
using AIThread.Common.Enums;

namespace AIThread.Service.Implementations;

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
                if (GetString(env["ANTHROPIC_CUSTOM_HEADERS"]) is { } headers && IsAIThreadHeader(headers))
                    env.Remove("ANTHROPIC_CUSTOM_HEADERS");
                if (env.Count == 0) root.Remove("env");
            }
            ClaudePermissionRules.Remove(root);
            await WriteAsync(target.Path, root, cancellationToken);
        }

        return await GetStatusAsync(scope, projectId, proxyBaseUrl, cancellationToken);
    }
}

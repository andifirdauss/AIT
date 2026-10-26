using System.Text.Json.Nodes;
using AIThread.Common.DTOs;
using AIThread.Common.Localization;
using AIThread.Service.Models;

namespace AIThread.Service.Implementations;

public sealed partial class ClaudeSettingsService
{
    public async Task<ClaudeSettingsStatusDto> ApplyAsync(ClaudeSettingsApplyRequest request, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(request.ProxyBaseUrl, UriKind.Absolute, out _))
            throw new AppValidationException(Lang.T("URL proxy tidak valid.", "Invalid proxy URL."));

        var target = await ResolveTargetAsync(request.Scope, request.ProjectId, cancellationToken);
        var root = await ReadAsync(target.Path, cancellationToken);
        await BackupOnceAsync(target.Path, cancellationToken);

        var env = root["env"] as JsonObject ?? new JsonObject();
        root["env"] = env;
        foreach (var (key, value) in await BuildEnvAsync(request.ProxyBaseUrl, request.TimeoutMs, target.ProjectRoot, cancellationToken))
        {
            // Never overwrite custom headers the user configured themselves.
            if (key == "ANTHROPIC_CUSTOM_HEADERS" && GetString(env[key]) is { } existing && !IsAIThreadHeader(existing))
                continue;
            env[key] = value?.DeepClone();
        }
        // With its requests going through AIT, Claude Code also stops reading credential files itself.
        ClaudePermissionRules.Apply(root);

        await WriteAsync(target.Path, root, cancellationToken);
        return await GetStatusAsync(request.Scope, request.ProjectId, request.ProxyBaseUrl, cancellationToken);
    }
}

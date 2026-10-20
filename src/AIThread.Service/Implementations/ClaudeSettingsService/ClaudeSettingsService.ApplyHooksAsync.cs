using AIThread.Common.DTOs;
using AIThread.Service.Models;

namespace AIThread.Service.Implementations;

public sealed partial class ClaudeSettingsService
{
    public async Task<ClaudeHooksStatusDto> ApplyHooksAsync(ClaudeSettingsApplyRequest request, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(request.ProxyBaseUrl, UriKind.Absolute, out _))
            throw new AppValidationException("URL AIT tidak valid.");

        var target = await ResolveTargetAsync(request.Scope, request.ProjectId, cancellationToken);
        var root = await ReadAsync(target.Path, cancellationToken);
        await BackupOnceAsync(target.Path, cancellationToken);
        ClaudeHookConfig.Apply(root, request.ProxyBaseUrl);
        await WriteAsync(target.Path, root, cancellationToken);
        return await GetHooksStatusAsync(request.Scope, request.ProjectId, request.ProxyBaseUrl, cancellationToken);
    }
}

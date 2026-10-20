using AIThread.Common.DTOs;
using AIThread.Common.Enums;
using AIThread.Service.Models;

namespace AIThread.Service.Implementations.Cli;

public abstract partial class FileCliIntegration
{
    public async Task<CliStatusDto> ApplyAsync(CliScope scope, Guid? projectId, string proxyBaseUrl, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(proxyBaseUrl, UriKind.Absolute, out _)) throw new AppValidationException("URL proxy tidak valid.");

        var (path, projectRoot) = await ResolveAsync(scope, projectId, cancellationToken);
        var updated = Apply(await ReadAsync(path, cancellationToken), await EntryAsync(proxyBaseUrl, projectRoot, cancellationToken));
        await BackupOnceAsync(path, cancellationToken);
        await WriteAsync(path, updated, cancellationToken);
        return await GetStatusAsync(scope, projectId, proxyBaseUrl, cancellationToken);
    }
}

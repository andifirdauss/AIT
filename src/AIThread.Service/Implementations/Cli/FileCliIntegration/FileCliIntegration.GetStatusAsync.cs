using AIThread.Common.DTOs;
using AIThread.Common.Enums;

namespace AIThread.Service.Implementations.Cli;

public abstract partial class FileCliIntegration
{
    public async Task<CliStatusDto> GetStatusAsync(CliScope scope, Guid? projectId, string proxyBaseUrl, CancellationToken cancellationToken = default)
    {
        var (path, projectRoot) = await ResolveAsync(scope, projectId, cancellationToken);
        var current = CurrentBaseUrl(await ReadAsync(path, cancellationToken));
        var entry = await EntryAsync(proxyBaseUrl, projectRoot, cancellationToken);
        return new CliStatusDto(Info.Id, scope, path, File.Exists(path), SameUrl(current, entry.BaseUrl), current, Preview(entry));
    }
}

using AiThread.Common.DTOs;
using AiThread.Common.Enums;

namespace AiThread.Service.Implementations.Cli;

public abstract partial class FileCliIntegration
{
    public async Task<CliStatusDto> RevertAsync(CliScope scope, Guid? projectId, string proxyBaseUrl, CancellationToken cancellationToken = default)
    {
        var (path, _) = await ResolveAsync(scope, projectId, cancellationToken);
        if (File.Exists(path))
        {
            var remaining = Remove(await ReadAsync(path, cancellationToken));
            // A file that only held AiThread's entry goes away with it.
            if (string.IsNullOrWhiteSpace(remaining) && !File.Exists(path + BackupSuffix)) File.Delete(path);
            else await WriteAsync(path, remaining, cancellationToken);
        }
        return await GetStatusAsync(scope, projectId, proxyBaseUrl, cancellationToken);
    }
}

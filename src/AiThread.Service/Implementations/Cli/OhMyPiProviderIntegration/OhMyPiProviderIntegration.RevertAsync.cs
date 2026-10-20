using AiThread.Common.DTOs;
using AiThread.Common.Enums;

namespace AiThread.Service.Implementations.Cli;

public sealed partial class OhMyPiProviderIntegration
{
    public async Task<CliStatusDto> RevertAsync(CliScope scope, Guid? projectId, string proxyBaseUrl, CancellationToken cancellationToken = default)
    {
        EnsureGlobal(scope);
        if (File.Exists(_modelsPath))
        {
            var remaining = YamlProviderBlock.Remove(await ReadAsync(cancellationToken));
            // A models.yml that only held our provider goes away with it.
            if (string.IsNullOrWhiteSpace(remaining) && !File.Exists(_modelsPath + BackupSuffix)) File.Delete(_modelsPath);
            else await WriteAsync(remaining, cancellationToken);
        }
        return await GetStatusAsync(scope, projectId, proxyBaseUrl, cancellationToken);
    }
}

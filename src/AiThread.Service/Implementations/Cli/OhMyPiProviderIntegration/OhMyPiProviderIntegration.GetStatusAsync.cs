using AiThread.Common.DTOs;
using AiThread.Common.Enums;

namespace AiThread.Service.Implementations.Cli;

public sealed partial class OhMyPiProviderIntegration
{
    public async Task<CliStatusDto> GetStatusAsync(CliScope scope, Guid? projectId, string proxyBaseUrl, CancellationToken cancellationToken = default)
    {
        EnsureGlobal(scope);
        var content = await ReadAsync(cancellationToken);
        var (enabled, current) = Inspect(content, proxyBaseUrl);
        var preview = string.Join("\n", new[] { YamlProviderBlock.Begin, "providers:" }
            .Concat((await ProviderLinesAsync(proxyBaseUrl, cancellationToken)).Select(l => "  " + l))
            .Append(YamlProviderBlock.End));
        return new CliStatusDto(Id, CliScope.Global, _modelsPath, File.Exists(_modelsPath), enabled, current, preview);
    }
}

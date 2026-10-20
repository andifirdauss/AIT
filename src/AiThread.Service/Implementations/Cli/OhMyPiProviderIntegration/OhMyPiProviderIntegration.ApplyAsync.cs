using AiThread.Common.DTOs;
using AiThread.Common.Enums;
using AiThread.Service.Models;

namespace AiThread.Service.Implementations.Cli;

public sealed partial class OhMyPiProviderIntegration
{
    public async Task<CliStatusDto> ApplyAsync(CliScope scope, Guid? projectId, string proxyBaseUrl, CancellationToken cancellationToken = default)
    {
        EnsureGlobal(scope);
        if (!Uri.TryCreate(proxyBaseUrl, UriKind.Absolute, out _)) throw new AppValidationException("URL proxy tidak valid.");

        // omp migrates an old models.json into models.yml only while models.yml does not exist yet.
        var legacy = Path.Combine(Path.GetDirectoryName(_modelsPath)!, "models.json");
        if (!File.Exists(_modelsPath) && File.Exists(legacy))
            throw new AppValidationException($"Ada {legacy} versi lama. Jalankan omp sekali agar dipindah ke models.yml, lalu nyalakan lagi.");

        var content = await ReadAsync(cancellationToken);
        if (YamlProviderBlock.Conflict(content, ProviderId) is { } conflict) throw new AppValidationException(conflict);

        var lines = await ProviderLinesAsync(proxyBaseUrl, cancellationToken);
        await BackupOnceAsync(cancellationToken);
        await WriteAsync(YamlProviderBlock.Apply(content, ProviderId, lines), cancellationToken);
        return await GetStatusAsync(scope, projectId, proxyBaseUrl, cancellationToken);
    }
}

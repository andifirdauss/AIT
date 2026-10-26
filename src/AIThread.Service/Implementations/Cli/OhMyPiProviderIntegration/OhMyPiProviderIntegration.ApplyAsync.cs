using AIThread.Common.DTOs;
using AIThread.Common.Enums;
using AIThread.Common.Localization;
using AIThread.Service.Models;

namespace AIThread.Service.Implementations.Cli;

public sealed partial class OhMyPiProviderIntegration
{
    public async Task<CliStatusDto> ApplyAsync(CliScope scope, Guid? projectId, string proxyBaseUrl, CancellationToken cancellationToken = default)
    {
        EnsureGlobal(scope);
        if (!Uri.TryCreate(proxyBaseUrl, UriKind.Absolute, out _)) throw new AppValidationException(Lang.T("URL proxy tidak valid.", "Invalid proxy URL."));

        // omp migrates an old models.json into models.yml only while models.yml does not exist yet.
        var legacy = Path.Combine(Path.GetDirectoryName(_modelsPath)!, "models.json");
        if (!File.Exists(_modelsPath) && File.Exists(legacy))
            throw new AppValidationException(Lang.T($"Ada {legacy} versi lama. Jalankan omp sekali agar dipindah ke models.yml, lalu nyalakan lagi.", $"{legacy} from an old version exists. Run omp once so it is moved to models.yml, then turn it on again."));

        var content = await ReadAsync(cancellationToken);
        if (YamlProviderBlock.Conflict(content, ProviderId) is { } conflict) throw new AppValidationException(conflict);

        var lines = await ProviderLinesAsync(proxyBaseUrl, cancellationToken);
        await BackupOnceAsync(cancellationToken);
        await WriteAsync(YamlProviderBlock.Apply(content, ProviderId, lines), cancellationToken);
        return await GetStatusAsync(scope, projectId, proxyBaseUrl, cancellationToken);
    }
}

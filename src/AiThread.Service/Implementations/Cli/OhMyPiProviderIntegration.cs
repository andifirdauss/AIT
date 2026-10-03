using System.Text;
using System.Text.RegularExpressions;
using AiThread.Common.DTOs;
using AiThread.Common.Enums;
using AiThread.Service.Interfaces;
using AiThread.Service.Models;

namespace AiThread.Service.Implementations.Cli;

// AiThread as a provider of its own in oh-my-pi: a "providers.aithread" entry in ~/.omp/agent/models.yml, so omp lists
// "aithread/aithread" next to its built-in providers. Checked against omp 18.4.4: api anthropic-messages posts to <baseUrl>/v1/messages, apiKey is
// sent as Authorization: Bearer (an environment variable of that name wins over the literal). models.yml exists only
// per user (or per --profile), so there is no project scope; the project comes from the cwd omp sends.
public sealed partial class OhMyPiProviderIntegration(ISettingService settingService, string? modelsPath = null) : ICliIntegration
{
    public const string Id = "oh-my-pi-provider";
    public const string ProviderId = "aithread";
    public const string ModelId = "aithread";
    private const string BackupSuffix = ".aithread-backup";

    private readonly string _modelsPath = modelsPath ?? Path.Combine(OmpAgentDirectory, "models.yml");

    private static string OmpAgentDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".omp", "agent");

    public CliInfoDto Info => new(
        Id,
        "oh-my-pi (omp) — provider AiThread",
        $"Menambah provider \"{ProviderId}\" di models.yml omp. Pilih model {ProviderId}/{ModelId} lewat /model (atau --model {ProviderId}/{ModelId}); provider Anthropic asli tetap bisa dipakai.",
        _modelsPath,
        null,
        "Berlaku untuk sesi omp berikutnya. Project tetap terdeteksi dari folder tempat omp dijalankan.");

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

    // models.yml has no per-project counterpart.
    public Task<IReadOnlyList<ProjectCliStatusDto>> GetProjectStatusesAsync(string proxyBaseUrl, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ProjectCliStatusDto>>([]);

    // The provider entry, relative to its own indentation. AiThread picks the real models, so the window and output
    // limits only steer omp's own compaction and request size.
    private async Task<IReadOnlyList<string>> ProviderLinesAsync(string proxyBaseUrl, CancellationToken cancellationToken)
    {
        var settings = await settingService.GetRuntimeAsync(cancellationToken);
        return
        [
            $"{ProviderId}:",
            $"  baseUrl: {YamlProviderBlock.Quote(proxyBaseUrl.Trim().TrimEnd('/'))}",
            "  api: anthropic-messages",
            $"  apiKey: {YamlProviderBlock.Quote(settings.ProxyApiKey ?? "aithread-local")}",
            "  models:",
            $"    - id: {ModelId}",
            "      name: AiThread",
            "      contextWindow: 200000",
            "      maxTokens: 32000",
            "      input: [text, image]"
        ];
    }

    private static (bool Enabled, string? CurrentBaseUrl) Inspect(string content, string proxyBaseUrl)
    {
        if (YamlProviderBlock.Managed(content) is not { } block) return (false, null);
        var current = block.Select(l => BaseUrlLine().Match(l)).FirstOrDefault(m => m.Success)?.Groups["url"].Value;
        var enabled = current is not null && string.Equals(current.TrimEnd('/'), proxyBaseUrl.Trim().TrimEnd('/'), StringComparison.OrdinalIgnoreCase);
        return (enabled, current);
    }

    private static void EnsureGlobal(CliScope scope)
    {
        if (scope != CliScope.Global) throw new AppValidationException("models.yml oh-my-pi hanya ada di level global (per pengguna).");
    }

    private async Task<string> ReadAsync(CancellationToken cancellationToken) =>
        File.Exists(_modelsPath) ? await File.ReadAllTextAsync(_modelsPath, cancellationToken) : string.Empty;

    private async Task WriteAsync(string content, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_modelsPath)!);
        await File.WriteAllTextAsync(_modelsPath, content, new UTF8Encoding(false), cancellationToken);
    }

    private async Task BackupOnceAsync(CancellationToken cancellationToken)
    {
        var backup = _modelsPath + BackupSuffix;
        if (File.Exists(_modelsPath) && !File.Exists(backup))
            await File.WriteAllBytesAsync(backup, await File.ReadAllBytesAsync(_modelsPath, cancellationToken), cancellationToken);
    }

    [GeneratedRegex(@"^\s*baseUrl:\s*""?(?<url>[^""\s#]+)""?")]
    private static partial Regex BaseUrlLine();
}

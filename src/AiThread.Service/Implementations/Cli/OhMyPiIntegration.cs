using System.Text;
using AiThread.Common.DTOs;
using AiThread.Common.Enums;
using AiThread.Service.Interfaces;
using AiThread.Service.Models;
using AiThread.Service.Proxy;

namespace AiThread.Service.Implementations.Cli;

// oh-my-pi loads .env files into its environment before any provider lookup: <cwd>/.env first, then
// ~/.omp/agent/.env. The built-in anthropic provider reads
//   ANTHROPIC_BASE_URL       -> requests go to this proxy instead of api.anthropic.com
//   ANTHROPIC_API_KEY        -> sent as x-api-key and Authorization: Bearer
//   ANTHROPIC_CUSTOM_HEADERS -> extra headers ("name: value", comma or newline separated)
// (checked against omp 18.4.4). AiThread owns one delimited block of the file and leaves the rest alone.
public sealed class OhMyPiIntegration(ISettingService settingService, IProjectService projectService) : ICliIntegration
{
    public const string Id = "oh-my-pi";

    private const string BaseUrlKey = "ANTHROPIC_BASE_URL";
    private const string ApiKeyKey = "ANTHROPIC_API_KEY";
    private const string HeadersKey = "ANTHROPIC_CUSTOM_HEADERS";
    private const string BackupSuffix = ".aithread-backup";

    private static readonly string[] ManagedKeys = [BaseUrlKey, ApiKeyKey, HeadersKey];

    private static readonly string GlobalFile =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".omp", "agent", ".env");

    public CliInfoDto Info { get; } = new(
        Id,
        "oh-my-pi (omp)",
        "Menulis blok ANTHROPIC_BASE_URL / ANTHROPIC_API_KEY di file .env yang dibaca omp; berlaku untuk provider anthropic bawaan.",
        GlobalFile,
        ".env",
        "omp membaca .env dari folder tempat ia dijalankan (cwd): jalankan omp dari folder project ini. Bila omp login Anthropic (OAuth), token itu dipakai lebih dulu daripada API key di .env.");

    public async Task<CliStatusDto> GetStatusAsync(CliScope scope, Guid? projectId, string proxyBaseUrl, CancellationToken cancellationToken = default)
    {
        var target = await ResolveTargetAsync(scope, projectId, cancellationToken);
        var content = await ReadAsync(target.Path, cancellationToken);
        var entries = await BuildEntriesAsync(proxyBaseUrl, target.ProjectRoot, cancellationToken);
        var (enabled, current) = Inspect(content, proxyBaseUrl);
        return new CliStatusDto(Id, scope, target.Path, File.Exists(target.Path), enabled, current, EnvFileBlock.Render(entries, "\n"));
    }

    public async Task<CliStatusDto> ApplyAsync(CliScope scope, Guid? projectId, string proxyBaseUrl, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(proxyBaseUrl, UriKind.Absolute, out _))
            throw new AppValidationException("URL proxy tidak valid.");

        var target = await ResolveTargetAsync(scope, projectId, cancellationToken);
        var content = await ReadAsync(target.Path, cancellationToken);

        // A definition outside our block would shadow it or be shadowed, so ask the user to resolve it.
        var conflicts = ManagedKeys.Where(k => EnvFileBlock.GetUnmanaged(content, k) is not null).ToList();
        if (conflicts.Count > 0)
            throw new AppValidationException(
                $"{target.Path} sudah memuat {string.Join(", ", conflicts)} di luar blok AiThread. Hapus atau beri komentar baris itu dulu agar tidak bertabrakan.");

        var entries = await BuildEntriesAsync(proxyBaseUrl, target.ProjectRoot, cancellationToken);
        await BackupOnceAsync(target.Path, cancellationToken);
        await WriteAsync(target.Path, EnvFileBlock.Apply(content, entries), cancellationToken);
        return await GetStatusAsync(scope, projectId, proxyBaseUrl, cancellationToken);
    }

    public async Task<CliStatusDto> RevertAsync(CliScope scope, Guid? projectId, string proxyBaseUrl, CancellationToken cancellationToken = default)
    {
        var target = await ResolveTargetAsync(scope, projectId, cancellationToken);
        if (File.Exists(target.Path))
        {
            var remaining = EnvFileBlock.Remove(await ReadAsync(target.Path, cancellationToken));
            // A .env that only held our block goes away with it instead of lingering as an empty file.
            if (string.IsNullOrWhiteSpace(remaining) && !File.Exists(target.Path + BackupSuffix)) File.Delete(target.Path);
            else await WriteAsync(target.Path, remaining, cancellationToken);
        }
        return await GetStatusAsync(scope, projectId, proxyBaseUrl, cancellationToken);
    }

    public async Task<IReadOnlyList<ProjectCliStatusDto>> GetProjectStatusesAsync(string proxyBaseUrl, CancellationToken cancellationToken = default)
    {
        var statuses = new List<ProjectCliStatusDto>();
        foreach (var project in await projectService.GetAllAsync(cancellationToken))
        {
            var path = Path.Combine(project.RootPath, ".env");
            try
            {
                var (enabled, current) = Inspect(await ReadAsync(path, cancellationToken), proxyBaseUrl);
                statuses.Add(new ProjectCliStatusDto(project.Id, enabled, enabled ? null : current, path, null));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                statuses.Add(new ProjectCliStatusDto(project.Id, false, null, path, ex.Message));
            }
        }
        return statuses;
    }

    private async Task<List<KeyValuePair<string, string>>> BuildEntriesAsync(string proxyBaseUrl, string? projectRoot, CancellationToken cancellationToken)
    {
        var settings = await settingService.GetRuntimeAsync(cancellationToken);
        var entries = new List<KeyValuePair<string, string>>
        {
            new(BaseUrlKey, proxyBaseUrl.Trim().TrimEnd('/')),
            new(ApiKeyKey, settings.ProxyApiKey ?? "aithread-local")
        };

        // Pins the session to its project root. Left out when the path would break the header list (commas) or the
        // .env syntax (# starts a comment, quotes are parsed), since a wrong header is worse than none.
        if (projectRoot is not null && projectRoot.IndexOfAny([',', '#', '\'', '"', '`', '\r', '\n']) < 0)
            entries.Add(new(HeadersKey, $"{ClaudeRequestInspector.ProjectHeader}: {projectRoot}"));
        return entries;
    }

    // Enabled only when the managed block points at this proxy; otherwise report the URL found (managed or the user's own).
    private static (bool Enabled, string? CurrentBaseUrl) Inspect(string content, string proxyBaseUrl)
    {
        var current = EnvFileBlock.GetManaged(content, BaseUrlKey) ?? EnvFileBlock.GetUnmanaged(content, BaseUrlKey);
        var enabled = EnvFileBlock.GetManaged(content, BaseUrlKey) is { } managed && SameUrl(managed, proxyBaseUrl);
        return (enabled, current);
    }

    private async Task<(string Path, string? ProjectRoot)> ResolveTargetAsync(CliScope scope, Guid? projectId, CancellationToken cancellationToken)
    {
        if (scope == CliScope.Global) return (GlobalFile, null);

        if (projectId is null) throw new AppValidationException("Pilih project terlebih dahulu.");
        var project = await projectService.GetContextAsync(projectId.Value, cancellationToken)
                      ?? throw new AppValidationException("Project tidak ditemukan.");
        return (Path.Combine(project.RootPath, ".env"), project.RootPath);
    }

    private static async Task<string> ReadAsync(string path, CancellationToken cancellationToken) =>
        File.Exists(path) ? await File.ReadAllTextAsync(path, cancellationToken) : string.Empty;

    private static async Task WriteAsync(string path, string content, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, content, new UTF8Encoding(false), cancellationToken);
    }

    private static async Task BackupOnceAsync(string path, CancellationToken cancellationToken)
    {
        var backup = path + BackupSuffix;
        if (File.Exists(path) && !File.Exists(backup))
            await File.WriteAllBytesAsync(backup, await File.ReadAllBytesAsync(path, cancellationToken), cancellationToken);
    }

    private static bool SameUrl(string a, string b) =>
        string.Equals(a.Trim().TrimEnd('/'), b.Trim().TrimEnd('/'), StringComparison.OrdinalIgnoreCase);
}

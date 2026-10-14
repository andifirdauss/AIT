using System.Text;
using AiThread.Common.DTOs;
using AiThread.Common.Enums;
using AiThread.Service.Interfaces;
using AiThread.Service.Models;

namespace AiThread.Service.Implementations.Cli;

// A CLI whose AiThread entry lives in one config file per scope: Info.GlobalPath, and Info.ProjectFile inside a
// project folder (null = global only). Subclasses only transform the file's text; reading, the one-time
// "*.aithread-backup", project lookup and deleting a file that held nothing but AiThread's entry are shared.
public abstract class FileCliIntegration(ISettingService settingService, IProjectService? projectService = null) : ICliIntegration
{
    private const string BackupSuffix = ".aithread-backup";

    public abstract CliInfoDto Info { get; }

    // The URL the file holds for this proxy; CLIs that append "/messages" or "/responses" themselves want the /v1 root.
    protected virtual string ConfigBaseUrl(string proxyBaseUrl) => proxyBaseUrl.Trim().TrimEnd('/');

    // The base URL of AiThread's entry in the file, or null when it has none.
    protected abstract string? CurrentBaseUrl(string content);

    // The file with AiThread's entry added or replaced; throws AppValidationException when that is unsafe.
    protected abstract string Apply(string content, CliEntry entry);

    // The file without AiThread's entry.
    protected abstract string Remove(string content);

    // What Apply writes, for the "Yang akan ditulis" preview.
    protected abstract string Preview(CliEntry entry);

    public async Task<CliStatusDto> GetStatusAsync(CliScope scope, Guid? projectId, string proxyBaseUrl, CancellationToken cancellationToken = default)
    {
        var (path, projectRoot) = await ResolveAsync(scope, projectId, cancellationToken);
        var current = CurrentBaseUrl(await ReadAsync(path, cancellationToken));
        var entry = await EntryAsync(proxyBaseUrl, projectRoot, cancellationToken);
        return new CliStatusDto(Info.Id, scope, path, File.Exists(path), SameUrl(current, entry.BaseUrl), current, Preview(entry));
    }

    public async Task<CliStatusDto> ApplyAsync(CliScope scope, Guid? projectId, string proxyBaseUrl, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(proxyBaseUrl, UriKind.Absolute, out _)) throw new AppValidationException("URL proxy tidak valid.");

        var (path, projectRoot) = await ResolveAsync(scope, projectId, cancellationToken);
        var updated = Apply(await ReadAsync(path, cancellationToken), await EntryAsync(proxyBaseUrl, projectRoot, cancellationToken));
        await BackupOnceAsync(path, cancellationToken);
        await WriteAsync(path, updated, cancellationToken);
        return await GetStatusAsync(scope, projectId, proxyBaseUrl, cancellationToken);
    }

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

    public async Task<IReadOnlyList<ProjectCliStatusDto>> GetProjectStatusesAsync(string proxyBaseUrl, CancellationToken cancellationToken = default)
    {
        if (Info.ProjectFile is null || projectService is null) return [];

        var expected = ConfigBaseUrl(proxyBaseUrl);
        var statuses = new List<ProjectCliStatusDto>();
        foreach (var project in await projectService.GetAllAsync(cancellationToken))
        {
            var path = Path.Combine(project.RootPath, Info.ProjectFile);
            try
            {
                var current = CurrentBaseUrl(await ReadAsync(path, cancellationToken));
                var enabled = SameUrl(current, expected);
                statuses.Add(new ProjectCliStatusDto(project.Id, enabled, enabled ? null : current, path, null));
            }
            catch (Exception ex) when (ex is AppValidationException or IOException or UnauthorizedAccessException)
            {
                statuses.Add(new ProjectCliStatusDto(project.Id, false, null, path, ex.Message));
            }
        }
        return statuses;
    }

    private async Task<CliEntry> EntryAsync(string proxyBaseUrl, string? projectRoot, CancellationToken cancellationToken)
    {
        var settings = await settingService.GetRuntimeAsync(cancellationToken);
        return new CliEntry(ConfigBaseUrl(proxyBaseUrl), settings.ProxyApiKey ?? "aithread-local", projectRoot);
    }

    private async Task<(string Path, string? ProjectRoot)> ResolveAsync(CliScope scope, Guid? projectId, CancellationToken cancellationToken)
    {
        if (scope == CliScope.Global) return (Info.GlobalPath, null);

        if (Info.ProjectFile is null || projectService is null)
            throw new AppValidationException($"{Info.Name} hanya punya konfigurasi global (per pengguna).");
        if (projectId is null) throw new AppValidationException("Pilih project terlebih dahulu.");
        var project = await projectService.GetContextAsync(projectId.Value, cancellationToken)
                      ?? throw new AppValidationException("Project tidak ditemukan.");
        return (Path.Combine(project.RootPath, Info.ProjectFile), project.RootPath);
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

    private static bool SameUrl(string? current, string expected) =>
        current is not null && string.Equals(current.Trim().TrimEnd('/'), expected.TrimEnd('/'), StringComparison.OrdinalIgnoreCase);

    protected static string UserProfile => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
}

// What a CLI's config points at: BaseUrl already in the CLI's own form (see ConfigBaseUrl). ProjectRoot is set for
// a project-scope file.
public sealed record CliEntry(string BaseUrl, string ApiKey, string? ProjectRoot);

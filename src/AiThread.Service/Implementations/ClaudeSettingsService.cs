using System.Text.Json;
using System.Text.Json.Nodes;
using AiThread.Common.DTOs;
using AiThread.Common.Enums;
using AiThread.Service.Interfaces;
using AiThread.Service.Models;
using AiThread.Service.Proxy;

namespace AiThread.Service.Implementations;

// Claude Code reads env vars from the "env" block of settings.json:
//   ANTHROPIC_BASE_URL   -> requests go to this proxy instead of api.anthropic.com
//   ANTHROPIC_AUTH_TOKEN -> sent as "Authorization: Bearer"; also keeps real Anthropic credentials away from the proxy
//   API_TIMEOUT_MS       -> an orchestrated thread can take minutes
//   ANTHROPIC_CUSTOM_HEADERS (project scope only) -> pins the session to its project root folder
public sealed class ClaudeSettingsService(ISettingService settingService, IProjectService projectService) : IClaudeSettingsService
{
    private const string ProjectHeader = ClaudeRequestInspector.ProjectHeader;
    private const int DefaultTimeoutMs = 1_800_000;
    private const string BackupSuffix = ".aithread-backup";

    private static readonly string[] ManagedKeys = ["ANTHROPIC_BASE_URL", "ANTHROPIC_AUTH_TOKEN", "API_TIMEOUT_MS"];
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };
    private static readonly JsonDocumentOptions ReadOptions = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    public async Task<ClaudeSettingsStatusDto> GetStatusAsync(ClaudeSettingsScope scope, Guid? projectId, string proxyBaseUrl, CancellationToken cancellationToken = default)
    {
        var target = await ResolveTargetAsync(scope, projectId, cancellationToken);
        var root = await ReadAsync(target.Path, cancellationToken);
        var currentBaseUrl = GetString(root["env"]?["ANTHROPIC_BASE_URL"]);
        var env = await BuildEnvAsync(proxyBaseUrl, null, target.ProjectRoot, cancellationToken);

        return new ClaudeSettingsStatusDto(
            scope,
            target.Path,
            File.Exists(target.Path),
            currentBaseUrl is not null && SameUrl(currentBaseUrl, proxyBaseUrl),
            currentBaseUrl,
            new JsonObject { ["env"] = env }.ToJsonString(WriteOptions));
    }

    public async Task<ClaudeSettingsStatusDto> ApplyAsync(ClaudeSettingsApplyRequest request, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(request.ProxyBaseUrl, UriKind.Absolute, out _))
            throw new AppValidationException("URL proxy tidak valid.");

        var target = await ResolveTargetAsync(request.Scope, request.ProjectId, cancellationToken);
        var root = await ReadAsync(target.Path, cancellationToken);
        await BackupOnceAsync(target.Path, cancellationToken);

        var env = root["env"] as JsonObject ?? new JsonObject();
        root["env"] = env;
        foreach (var (key, value) in await BuildEnvAsync(request.ProxyBaseUrl, request.TimeoutMs, target.ProjectRoot, cancellationToken))
        {
            // Never overwrite custom headers the user configured themselves.
            if (key == "ANTHROPIC_CUSTOM_HEADERS" && GetString(env[key]) is { } existing && !IsAiThreadHeader(existing))
                continue;
            env[key] = value?.DeepClone();
        }

        await WriteAsync(target.Path, root, cancellationToken);
        return await GetStatusAsync(request.Scope, request.ProjectId, request.ProxyBaseUrl, cancellationToken);
    }

    public async Task<ClaudeSettingsStatusDto> RevertAsync(ClaudeSettingsScope scope, Guid? projectId, string proxyBaseUrl, CancellationToken cancellationToken = default)
    {
        var target = await ResolveTargetAsync(scope, projectId, cancellationToken);
        if (File.Exists(target.Path))
        {
            var root = await ReadAsync(target.Path, cancellationToken);
            if (root["env"] is JsonObject env)
            {
                foreach (var key in ManagedKeys) env.Remove(key);
                if (GetString(env["ANTHROPIC_CUSTOM_HEADERS"]) is { } headers && IsAiThreadHeader(headers))
                    env.Remove("ANTHROPIC_CUSTOM_HEADERS");
                if (env.Count == 0) root.Remove("env");
            }
            await WriteAsync(target.Path, root, cancellationToken);
        }

        return await GetStatusAsync(scope, projectId, proxyBaseUrl, cancellationToken);
    }

    public async Task<IReadOnlyList<ProjectClaudeIntegrationDto>> GetProjectStatusesAsync(string proxyBaseUrl, CancellationToken cancellationToken = default)
    {
        var statuses = new List<ProjectClaudeIntegrationDto>();
        foreach (var project in await projectService.GetAllAsync(cancellationToken))
        {
            var path = ProjectSettingsPath(project.RootPath);
            try
            {
                var baseUrl = GetString((await ReadAsync(path, cancellationToken))["env"]?["ANTHROPIC_BASE_URL"]);
                var enabled = baseUrl is not null && SameUrl(baseUrl, proxyBaseUrl);
                statuses.Add(new ProjectClaudeIntegrationDto(project.Id, enabled, enabled ? null : baseUrl, path, null));
            }
            catch (Exception ex) when (ex is AppValidationException or IOException or UnauthorizedAccessException)
            {
                statuses.Add(new ProjectClaudeIntegrationDto(project.Id, false, null, path, ex.Message));
            }
        }
        return statuses;
    }

    public async Task<ClaudeHooksStatusDto> GetHooksStatusAsync(ClaudeSettingsScope scope, Guid? projectId, string baseUrl, CancellationToken cancellationToken = default)
    {
        var target = await ResolveTargetAsync(scope, projectId, cancellationToken);
        var installed = ClaudeHookConfig.InstalledBaseUrl(await ReadAsync(target.Path, cancellationToken));
        return new ClaudeHooksStatusDto(
            scope,
            target.Path,
            installed is not null && SameUrl(installed, baseUrl),
            installed,
            new JsonObject { ["hooks"] = ClaudeHookConfig.Block(baseUrl) }.ToJsonString(WriteOptions));
    }

    public async Task<ClaudeHooksStatusDto> ApplyHooksAsync(ClaudeSettingsApplyRequest request, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(request.ProxyBaseUrl, UriKind.Absolute, out _))
            throw new AppValidationException("URL AiThread tidak valid.");

        var target = await ResolveTargetAsync(request.Scope, request.ProjectId, cancellationToken);
        var root = await ReadAsync(target.Path, cancellationToken);
        await BackupOnceAsync(target.Path, cancellationToken);
        ClaudeHookConfig.Apply(root, request.ProxyBaseUrl);
        await WriteAsync(target.Path, root, cancellationToken);
        return await GetHooksStatusAsync(request.Scope, request.ProjectId, request.ProxyBaseUrl, cancellationToken);
    }

    public async Task<ClaudeHooksStatusDto> RevertHooksAsync(ClaudeSettingsScope scope, Guid? projectId, string baseUrl, CancellationToken cancellationToken = default)
    {
        var target = await ResolveTargetAsync(scope, projectId, cancellationToken);
        if (File.Exists(target.Path))
        {
            var root = await ReadAsync(target.Path, cancellationToken);
            ClaudeHookConfig.Remove(root);
            await WriteAsync(target.Path, root, cancellationToken);
        }
        return await GetHooksStatusAsync(scope, projectId, baseUrl, cancellationToken);
    }

    // Also recognizes the header of earlier versions (X-AiThread-Solution), so applying again replaces it.
    private static bool IsAiThreadHeader(string value) =>
        value.StartsWith(ProjectHeader, StringComparison.OrdinalIgnoreCase)
        || value.StartsWith(ClaudeRequestInspector.LegacySolutionHeader, StringComparison.OrdinalIgnoreCase);

    private static string ProjectSettingsPath(string rootPath) => Path.Combine(rootPath, ".claude", "settings.local.json");

    private async Task<JsonObject> BuildEnvAsync(string proxyBaseUrl, int? timeoutMs, string? projectRoot, CancellationToken cancellationToken)
    {
        var settings = await settingService.GetRuntimeAsync(cancellationToken);
        var env = new JsonObject
        {
            ["ANTHROPIC_BASE_URL"] = proxyBaseUrl.TrimEnd('/'),
            ["ANTHROPIC_AUTH_TOKEN"] = settings.ProxyApiKey ?? "aithread-local",
            ["API_TIMEOUT_MS"] = (timeoutMs is > 0 ? timeoutMs.Value : DefaultTimeoutMs).ToString()
        };
        if (projectRoot is not null) env["ANTHROPIC_CUSTOM_HEADERS"] = $"{ProjectHeader}: {projectRoot}";
        return env;
    }

    private async Task<(string Path, string? ProjectRoot)> ResolveTargetAsync(ClaudeSettingsScope scope, Guid? projectId, CancellationToken cancellationToken)
    {
        if (scope == ClaudeSettingsScope.User)
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return (Path.Combine(home, ".claude", "settings.json"), null);
        }

        if (projectId is null) throw new AppValidationException("Pilih project terlebih dahulu.");
        var project = await projectService.GetContextAsync(projectId.Value, cancellationToken)
                      ?? throw new AppValidationException("Project tidak ditemukan.");
        return (ProjectSettingsPath(project.RootPath), project.RootPath);
    }

    private static async Task<JsonObject> ReadAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) return new JsonObject();
        var text = await File.ReadAllTextAsync(path, cancellationToken);
        if (string.IsNullOrWhiteSpace(text)) return new JsonObject();

        try
        {
            return JsonNode.Parse(text, documentOptions: ReadOptions) as JsonObject
                   ?? throw new AppValidationException($"{path} bukan objek JSON.");
        }
        catch (JsonException ex)
        {
            throw new AppValidationException($"{path} tidak dapat dibaca sebagai JSON: {ex.Message}");
        }
    }

    private static async Task WriteAsync(string path, JsonObject root, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, root.ToJsonString(WriteOptions) + Environment.NewLine, cancellationToken);
    }

    private static async Task BackupOnceAsync(string path, CancellationToken cancellationToken)
    {
        var backup = path + BackupSuffix;
        if (File.Exists(path) && !File.Exists(backup))
            await File.WriteAllBytesAsync(backup, await File.ReadAllBytesAsync(path, cancellationToken), cancellationToken);
    }

    private static bool SameUrl(string a, string b) =>
        string.Equals(a.Trim().TrimEnd('/'), b.Trim().TrimEnd('/'), StringComparison.OrdinalIgnoreCase);

    private static string? GetString(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
}

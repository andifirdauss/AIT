using System.Text.Json.Nodes;
using AiThread.Common.DTOs;
using AiThread.Service.Interfaces;
using AiThread.Service.Models;

namespace AiThread.Service.Implementations.Cli;

// opencode: a "provider.aithread" entry (Anthropic Messages via @ai-sdk/anthropic, which posts to <baseURL>/messages)
// in the global opencode.json, so "aithread/aithread" shows up in /models next to the user's other providers.
// Global only: a project's opencode.json is usually committed, and the entry carries the proxy key. opencode's
// default 5-minute request timeout would cut an orchestrated thread off, so it is disabled for this provider.
public sealed class OpenCodeIntegration(ISettingService settingService, string? configPath = null) : FileCliIntegration(settingService)
{
    public const string Id = "opencode";
    public const string ProviderId = "aithread";
    public const string ModelId = "aithread";
    private const string DisplayName = "AIT";

    private readonly string _configPath = configPath ?? DefaultPath();

    public override CliInfoDto Info => new(
        Id,
        "opencode — provider AIT",
        $"Menambah provider \"{ProviderId}\" di opencode.json global. Pilih model {ProviderId}/{ModelId} lewat /models (atau --model {ProviderId}/{ModelId}); provider lain tetap bisa dipakai.",
        _configPath,
        null,
        "Berlaku untuk sesi opencode berikutnya. Project terdeteksi dari folder tempat opencode dijalankan.");

    // $XDG_CONFIG_HOME/opencode (default ~/.config/opencode, also on Windows); an existing opencode.jsonc is used as is.
    private static string DefaultPath()
    {
        var configHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } xdg ? xdg : Path.Combine(UserProfile, ".config");
        var directory = Path.Combine(configHome, "opencode");
        var json = Path.Combine(directory, "opencode.json");
        var jsonc = Path.Combine(directory, "opencode.jsonc");
        return !File.Exists(json) && File.Exists(jsonc) ? jsonc : json;
    }

    protected override string ConfigBaseUrl(string proxyBaseUrl) => base.ConfigBaseUrl(proxyBaseUrl) + "/v1";

    protected override string? CurrentBaseUrl(string content) =>
        JsonConfigFile.GetString(JsonConfigFile.At(JsonConfigFile.Read(content, FileName), "provider", ProviderId, "options", "baseURL"));

    protected override string Apply(string content, CliEntry entry)
    {
        var root = JsonConfigFile.ReadForWrite(content, FileName);
        var providers = root["provider"] as JsonObject;
        if (root["provider"] is not null && providers is null) throw new AppValidationException($"\"provider\" di {FileName} bukan objek.");
        if (providers?[ProviderId] is { } existing && JsonConfigFile.GetString(JsonConfigFile.At(existing, "name")) != DisplayName)
            throw new AppValidationException($"{FileName} sudah punya provider \"{ProviderId}\" buatan Anda. Hapus atau ganti namanya dulu.");

        providers ??= new JsonObject();
        root["provider"] = providers;
        providers[ProviderId] = Provider(entry);
        return JsonConfigFile.Write(root);
    }

    protected override string Remove(string content)
    {
        if (JsonConfigFile.At(JsonConfigFile.Read(content, FileName), "provider", ProviderId) is null) return content;
        var root = JsonConfigFile.ReadForWrite(content, FileName);
        var providers = (JsonObject)root["provider"]!;
        providers.Remove(ProviderId);
        if (providers.Count == 0) root.Remove("provider");
        return root.Count == 0 ? string.Empty : JsonConfigFile.Write(root);
    }

    protected override string Preview(CliEntry entry) =>
        JsonConfigFile.Write(new JsonObject { ["provider"] = new JsonObject { [ProviderId] = Provider(entry) } }).TrimEnd();

    private static JsonObject Provider(CliEntry entry) => new()
    {
        ["npm"] = "@ai-sdk/anthropic",
        ["name"] = DisplayName,
        ["options"] = new JsonObject
        {
            ["baseURL"] = entry.BaseUrl,
            ["apiKey"] = entry.ApiKey,
            ["timeout"] = false
        },
        ["models"] = new JsonObject
        {
            [ModelId] = new JsonObject
            {
                ["name"] = DisplayName,
                ["modalities"] = new JsonObject { ["input"] = new JsonArray("text", "image"), ["output"] = new JsonArray("text") },
                ["limit"] = new JsonObject { ["context"] = 1_000_000, ["output"] = 32_000 }
            }
        }
    };

    private string FileName => Path.GetFileName(_configPath);
}

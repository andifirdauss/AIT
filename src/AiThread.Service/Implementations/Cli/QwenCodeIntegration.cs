using System.Text.Json.Nodes;
using AiThread.Common.DTOs;
using AiThread.Service.Interfaces;
using AiThread.Service.Models;

namespace AiThread.Service.Implementations.Cli;

// Qwen Code: a model "aithread" under modelProviders.openai (Chat Completions at <baseUrl>/chat/completions) in
// ~/.qwen/settings.json, its key in env.AITHREAD_API_KEY, so it can be picked with /model next to the user's other
// models. Global only: Qwen Code replaces, not merges, modelProviders from a project's settings.json. The long
// request timeout keeps an orchestrated thread from being cut off.
public sealed class QwenCodeIntegration(ISettingService settingService, string? settingsPath = null) : FileCliIntegration(settingService)
{
    public const string Id = "qwen-code";
    public const string ModelId = "aithread";
    public const string EnvKey = "AITHREAD_API_KEY";
    private const string DisplayName = "AIT";
    private const string FileName = "settings.json";

    private readonly string _settingsPath = settingsPath ?? Path.Combine(UserProfile, ".qwen", FileName);

    public override CliInfoDto Info => new(
        Id,
        "Qwen Code — model AIT",
        $"Menambah model \"{ModelId}\" (OpenAI-compatible) di modelProviders settings.json Qwen Code beserta {EnvKey} di blok env. Pilih model {DisplayName} lewat /model; model lain tetap bisa dipakai.",
        _settingsPath,
        null,
        "Berlaku untuk sesi Qwen Code berikutnya. Project terdeteksi dari folder tempat qwen dijalankan.");

    protected override string ConfigBaseUrl(string proxyBaseUrl) => base.ConfigBaseUrl(proxyBaseUrl) + "/v1";

    protected override string? CurrentBaseUrl(string content) =>
        JsonConfigFile.GetString(FindModel(JsonConfigFile.Read(content, FileName))?["baseUrl"]);

    protected override string Apply(string content, CliEntry entry)
    {
        var root = JsonConfigFile.ReadForWrite(content, FileName);
        var providers = Object(root, "modelProviders");
        var openAi = providers["openai"] switch
        {
            null => new JsonArray(),
            JsonArray array => array,
            _ => throw new AppValidationException($"modelProviders.openai di {FileName} bukan array.")
        };
        providers["openai"] = openAi;

        if (FindModel(root) is { } existing)
        {
            if (JsonConfigFile.GetString(existing["name"]) != DisplayName)
                throw new AppValidationException($"{FileName} sudah punya model \"{ModelId}\" buatan Anda di modelProviders.openai. Hapus atau ganti id-nya dulu.");
            openAi.Remove(existing);
        }
        openAi.Add(Model(entry));
        Object(root, "env")[EnvKey] = entry.ApiKey;
        return JsonConfigFile.Write(root);
    }

    protected override string Remove(string content)
    {
        var lenient = JsonConfigFile.Read(content, FileName);
        if (FindModel(lenient) is null && JsonConfigFile.At(lenient, "env", EnvKey) is null) return content;

        var root = JsonConfigFile.ReadForWrite(content, FileName);
        if (FindModel(root) is { } model && JsonConfigFile.At(root, "modelProviders", "openai") is JsonArray openAi)
        {
            openAi.Remove(model);
            var providers = (JsonObject)root["modelProviders"]!;
            if (openAi.Count == 0) providers.Remove("openai");
            if (providers.Count == 0) root.Remove("modelProviders");
        }
        if (root["env"] is JsonObject env)
        {
            env.Remove(EnvKey);
            if (env.Count == 0) root.Remove("env");
        }
        return root.Count == 0 ? string.Empty : JsonConfigFile.Write(root);
    }

    protected override string Preview(CliEntry entry) =>
        JsonConfigFile.Write(new JsonObject
        {
            ["modelProviders"] = new JsonObject { ["openai"] = new JsonArray(Model(entry)) },
            ["env"] = new JsonObject { [EnvKey] = entry.ApiKey }
        }).TrimEnd();

    private static JsonObject Model(CliEntry entry) => new()
    {
        ["id"] = ModelId,
        ["name"] = DisplayName,
        ["description"] = "Orkestrasi AIT (model asli dipilih AIT)",
        ["envKey"] = EnvKey,
        ["baseUrl"] = entry.BaseUrl,
        ["generationConfig"] = new JsonObject
        {
            ["timeout"] = 1_800_000,
            ["contextWindowSize"] = 1_000_000,
            ["modalities"] = new JsonObject { ["image"] = true }
        }
    };

    private static JsonObject? FindModel(JsonObject root) =>
        (JsonConfigFile.At(root, "modelProviders", "openai") as JsonArray)?.OfType<JsonObject>()
            .FirstOrDefault(m => JsonConfigFile.GetString(m["id"]) == ModelId);

    private static JsonObject Object(JsonObject parent, string key)
    {
        switch (parent[key])
        {
            case JsonObject existing:
                return existing;
            case null:
                var created = new JsonObject();
                parent[key] = created;
                return created;
            default:
                throw new AppValidationException($"\"{key}\" di {FileName} bukan objek.");
        }
    }
}

using AIThread.Common.DTOs;
using AIThread.Common.Localization;
using AIThread.Service.Interfaces;

namespace AIThread.Service.Implementations.Cli;

// Qwen Code: a model "aithread" under modelProviders.openai (Chat Completions at <baseUrl>/chat/completions) in
// ~/.qwen/settings.json, its key in env.AITHREAD_API_KEY, so it can be picked with /model next to the user's other
// models. Global only: Qwen Code replaces, not merges, modelProviders from a project's settings.json. The long
// request timeout keeps an orchestrated thread from being cut off.
public sealed partial class QwenCodeIntegration(ISettingService settingService, string? settingsPath = null) : FileCliIntegration(settingService)
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
        Lang.T($"Menambah model \"{ModelId}\" (OpenAI-compatible) di modelProviders settings.json Qwen Code beserta {EnvKey} di blok env. Pilih model {DisplayName} lewat /model; model lain tetap bisa dipakai.", $"Adds the model \"{ModelId}\" (OpenAI-compatible) to modelProviders in the Qwen Code settings.json, with {EnvKey} in the env block. Choose the model {DisplayName} with /model; other models can still be used."),
        _settingsPath,
        null,
        Lang.T("Berlaku untuk sesi Qwen Code berikutnya. Project terdeteksi dari folder tempat qwen dijalankan.", "Takes effect for the next Qwen Code session. The project is detected from the folder qwen runs in."));
}

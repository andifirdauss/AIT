using AIThread.Common.DTOs;
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
        $"Menambah model \"{ModelId}\" (OpenAI-compatible) di modelProviders settings.json Qwen Code beserta {EnvKey} di blok env. Pilih model {DisplayName} lewat /model; model lain tetap bisa dipakai.",
        _settingsPath,
        null,
        "Berlaku untuk sesi Qwen Code berikutnya. Project terdeteksi dari folder tempat qwen dijalankan.");
}

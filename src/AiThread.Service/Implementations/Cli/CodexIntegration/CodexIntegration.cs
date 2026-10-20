using AiThread.Common.DTOs;
using AiThread.Service.Interfaces;

namespace AiThread.Service.Implementations.Cli;

// Codex CLI: a "model_providers.aithread" entry plus a "profiles.aithread" profile at the end of
// $CODEX_HOME/config.toml (default ~/.codex), so "codex --profile aithread" goes through AiThread while plain codex
// keeps its own provider. Codex strips model_provider(s) and profiles from a project's .codex/config.toml, so there
// is no project scope. Codex only speaks the Responses API to a custom provider; the key is sent as
// Authorization: Bearer. An orchestrated thread can stay quiet for minutes, hence the long stream idle timeout.
public sealed partial class CodexIntegration(ISettingService settingService, string? configPath = null) : FileCliIntegration(settingService)
{
    public const string Id = "codex";
    public const string ProviderId = "aithread";
    private const string FileName = "config.toml";

    private readonly string _configPath = configPath ?? Path.Combine(CodexHome, FileName);

    private static string CodexHome =>
        Environment.GetEnvironmentVariable("CODEX_HOME") is { Length: > 0 } home ? home : Path.Combine(UserProfile, ".codex");

    public override CliInfoDto Info => new(
        Id,
        "Codex CLI — profile AIT",
        $"Menambah provider dan profile \"{ProviderId}\" di config.toml Codex. Jalankan codex --profile {ProviderId} (atau -p {ProviderId}); codex tanpa profile tetap memakai provider aslinya.",
        _configPath,
        null,
        $"Berlaku untuk sesi codex berikutnya yang dijalankan dengan --profile {ProviderId}. Project terdeteksi dari folder tempat codex dijalankan.");
}

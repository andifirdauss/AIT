using AIThread.Common.DTOs;
using AIThread.Common.Localization;
using AIThread.Service.Interfaces;

namespace AIThread.Service.Implementations.Cli;

// Codex CLI: a "model_providers.aithread" entry plus a "profiles.aithread" profile at the end of
// $CODEX_HOME/config.toml (default ~/.codex), so "codex --profile aithread" goes through AIThread while plain codex
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
        Lang.T($"Menambah provider dan profile \"{ProviderId}\" di config.toml Codex. Jalankan codex --profile {ProviderId} (atau -p {ProviderId}); codex tanpa profile tetap memakai provider aslinya.", $"Adds the provider and profile \"{ProviderId}\" to the Codex config.toml. Run codex --profile {ProviderId} (or -p {ProviderId}); codex without the profile keeps its original provider."),
        _configPath,
        null,
        Lang.T($"Berlaku untuk sesi codex berikutnya yang dijalankan dengan --profile {ProviderId}. Project terdeteksi dari folder tempat codex dijalankan.", $"Takes effect for the next codex session run with --profile {ProviderId}. The project is detected from the folder codex runs in."));
}

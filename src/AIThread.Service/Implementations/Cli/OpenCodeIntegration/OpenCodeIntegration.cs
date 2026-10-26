using AIThread.Common.DTOs;
using AIThread.Common.Localization;
using AIThread.Service.Interfaces;

namespace AIThread.Service.Implementations.Cli;

// opencode: a "provider.aithread" entry (Anthropic Messages via @ai-sdk/anthropic, which posts to <baseURL>/messages)
// in the global opencode.json, so "aithread/aithread" shows up in /models next to the user's other providers.
// Global only: a project's opencode.json is usually committed, and the entry carries the proxy key. opencode's
// default 5-minute request timeout would cut an orchestrated thread off, so it is disabled for this provider.
public sealed partial class OpenCodeIntegration(ISettingService settingService, string? configPath = null) : FileCliIntegration(settingService)
{
    public const string Id = "opencode";
    public const string ProviderId = "aithread";
    public const string ModelId = "aithread";
    private const string DisplayName = "AIT";

    private readonly string _configPath = configPath ?? DefaultPath();

    public override CliInfoDto Info => new(
        Id,
        "opencode — provider AIT",
        Lang.T($"Menambah provider \"{ProviderId}\" di opencode.json global. Pilih model {ProviderId}/{ModelId} lewat /models (atau --model {ProviderId}/{ModelId}); provider lain tetap bisa dipakai.", $"Adds the provider \"{ProviderId}\" to the global opencode.json. Choose the model {ProviderId}/{ModelId} with /models (or --model {ProviderId}/{ModelId}); other providers can still be used."),
        _configPath,
        null,
        Lang.T("Berlaku untuk sesi opencode berikutnya. Project terdeteksi dari folder tempat opencode dijalankan.", "Takes effect for the next opencode session. The project is detected from the folder opencode runs in."));

    private string FileName => Path.GetFileName(_configPath);
}

using AIThread.Common.DTOs;
using AIThread.Common.Localization;
using AIThread.Service.Interfaces;

namespace AIThread.Service.Implementations.Cli;

// Gemini CLI: GOOGLE_GEMINI_BASE_URL and GEMINI_API_KEY in a .gemini/.env file, ~/.gemini/.env globally or
// <project>/.gemini/.env per project. Gemini CLI loads only the first .env it finds walking up from the cwd (a
// project's .gemini/.env or .env before the home one), and the base URL only applies with "gemini-api-key" auth.
// The SDK appends /v1beta/models/... itself, so the base URL is the bare proxy address.
public sealed partial class GeminiCliIntegration(ISettingService settingService, IProjectService projectService, string? envPath = null)
    : FileCliIntegration(settingService, projectService)
{
    public const string Id = "gemini-cli";
    private const string BaseUrlKey = "GOOGLE_GEMINI_BASE_URL";
    private const string ApiKeyKey = "GEMINI_API_KEY";

    private readonly string _envPath = envPath ?? Path.Combine(UserProfile, ".gemini", ".env");

    public override CliInfoDto Info => new(
        Id,
        "Gemini CLI",
        Lang.T($"Menulis {BaseUrlKey} dan {ApiKeyKey} di file .gemini/.env yang dibaca Gemini CLI.", $"Writes {BaseUrlKey} and {ApiKeyKey} to the .gemini/.env file Gemini CLI reads."),
        _envPath,
        Path.Combine(".gemini", ".env"),
        Lang.T("Buka sesi Gemini CLI baru dan pilih \"Use Gemini API Key\" di /auth (login Google tidak lewat AIT). ", "Open a new Gemini CLI session and choose \"Use Gemini API Key\" in /auth (a Google login does not go through AIT). ") +
        Lang.T("Gemini CLI hanya membaca .env pertama yang ditemukan dari folder kerja ke atas, jadi .env di project menutupi yang global.", "Gemini CLI reads only the first .env it finds from the working folder upward, so a .env in the project hides the global one."));
}

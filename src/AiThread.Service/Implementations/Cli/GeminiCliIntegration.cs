using System.Text.RegularExpressions;
using AiThread.Common.DTOs;
using AiThread.Service.Interfaces;
using AiThread.Service.Models;

namespace AiThread.Service.Implementations.Cli;

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
        $"Menulis {BaseUrlKey} dan {ApiKeyKey} di file .gemini/.env yang dibaca Gemini CLI.",
        _envPath,
        Path.Combine(".gemini", ".env"),
        "Buka sesi Gemini CLI baru dan pilih \"Use Gemini API Key\" di /auth (login Google tidak lewat AIT). " +
        "Gemini CLI hanya membaca .env pertama yang ditemukan dari folder kerja ke atas, jadi .env di project menutupi yang global.");

    protected override string? CurrentBaseUrl(string content) =>
        ManagedTextBlock.Managed(content)?.Select(l => Assignment().Match(l))
            .FirstOrDefault(m => m.Success && m.Groups["key"].Value == BaseUrlKey)?.Groups["value"].Value;

    protected override string Apply(string content, CliEntry entry)
    {
        var own = ManagedTextBlock.Outside(content).Select(l => Assignment().Match(l))
            .FirstOrDefault(m => m.Success && m.Groups["key"].Value is BaseUrlKey or ApiKeyKey);
        if (own is not null)
            throw new AppValidationException($".env sudah berisi {own.Groups["key"].Value} buatan Anda. Hapus atau jadikan komentar dulu, lalu nyalakan lagi.");
        return ManagedTextBlock.Append(content, Block(entry));
    }

    protected override string Remove(string content) => ManagedTextBlock.Remove(content);

    protected override string Preview(CliEntry entry) =>
        string.Join("\n", Block(entry).Prepend(ManagedTextBlock.Begin).Append(ManagedTextBlock.End));

    private static IReadOnlyList<string> Block(CliEntry entry) =>
    [
        $"{BaseUrlKey}=\"{entry.BaseUrl}\"",
        $"{ApiKeyKey}=\"{entry.ApiKey}\""
    ];

    [GeneratedRegex(@"^\s*(export\s+)?(?<key>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*[""']?(?<value>[^""'\s#]*)")]
    private static partial Regex Assignment();
}

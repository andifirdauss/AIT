using AIThread.Common.DTOs;
using AIThread.Common.Options;
using AIThread.Service.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AIThread.Service.CliBackend;

public sealed partial class GeminiCliAdapter(IOptions<AIThreadOptions> options, ILogger<GeminiCliAdapter> logger) : IGeminiCliAdapter
{
    public const string DisplayName = "Gemini";
    private const string Command = "gemini";
    private const string NpmPackage = "@google/gemini-cli";

    private const string BaseUrlKey = "GOOGLE_GEMINI_BASE_URL";

    // Where a GOOGLE_GEMINI_BASE_URL pointing back at AIT is sent instead: Google's own Gemini API endpoint. The base
    // URL only applies to "Use Gemini API key" auth; a Google login ignores it.
    public const string OfficialBaseUrl = "https://generativelanguage.googleapis.com";

    // Gemini CLI exits with this code when it has no usable login (FatalAuthenticationError).
    private const int AuthExitCode = 41;

    private static readonly TimeSpan DetectCacheDuration = TimeSpan.FromMinutes(5);

    private readonly CliSlots _slots = new(options.Value.GeminiCli.MaxProcesses, options.Value.GeminiCli.BackgroundProcesses);
    private DetectCache? _detected;

    public string Tool => CliModelId.Gemini;

    public string Name => DisplayName;

    // Gemini CLI's model aliases: auto (its own routing), pro, flash, flash-lite follow Google's newest models.
    private static readonly IReadOnlyList<string> Models = ["auto", "pro", "flash", "flash-lite"];

    private sealed record DetectCache(CliBackendStatusDto Status, DateTime CheckedAt);
}

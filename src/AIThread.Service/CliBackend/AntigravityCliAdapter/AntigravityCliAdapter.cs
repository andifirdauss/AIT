using AIThread.Common.DTOs;
using AIThread.Common.Options;
using AIThread.Service.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AIThread.Service.CliBackend;

public sealed partial class AntigravityCliAdapter(IOptions<AIThreadOptions> options, ILogger<AntigravityCliAdapter> logger) : IAntigravityCliAdapter
{
    public const string DisplayName = "Antigravity";
    private const string Command = "agy";

    // The workspace agent each call runs as (.agents/agents/ait/agent.md in the working folder): no tools, AIT's system
    // prompt.
    public const string AgentName = "ait";

    // agy's data folder: its conversations, transcripts and settings.
    private static readonly string DataFolder =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gemini", "antigravity-cli");

    private static readonly TimeSpan DetectCacheDuration = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan ModelsCacheDuration = TimeSpan.FromMinutes(10);
    private static readonly CliCallLimits ModelsLimits = new(TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(60));

    private readonly CliSlots _slots = new(options.Value.AntigravityCli.MaxProcesses, options.Value.AntigravityCli.BackgroundProcesses);
    private DetectCache? _detected;
    private ModelsCache? _models;

    public string Tool => CliModelId.Antigravity;

    public string Name => DisplayName;

    private sealed record DetectCache(CliBackendStatusDto Status, DateTime CheckedAt);

    private sealed record ModelsCache(IReadOnlyList<string> Models, DateTime CheckedAt);
}

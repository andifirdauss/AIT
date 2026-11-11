using AIThread.Common.DTOs;
using AIThread.Common.Options;
using AIThread.Service.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AIThread.Service.CliBackend;

public sealed partial class CodexCliAdapter(IOptions<AIThreadOptions> options, ILogger<CodexCliAdapter> logger) : ICodexCliAdapter
{
    public const string DisplayName = "Codex";
    private const string Command = "codex";
    private const string NpmPackage = "@openai/codex";

    // The provider/profile id the CLI menu writes to config.toml (CodexIntegration.ProviderId): AIT's own proxy.
    private const string AitProvider = "aithread";

    private static readonly TimeSpan DetectCacheDuration = TimeSpan.FromMinutes(5);

    private readonly CliSlots _slots = new(options.Value.CodexCli.MaxProcesses, options.Value.CodexCli.BackgroundProcesses);
    private DetectCache? _detected;

    public string Tool => CliModelId.Codex;

    public string Name => DisplayName;

    // Codex's model names change with each release, so only its default model is offered; any name it knows can be
    // typed ("cli:codex:<model>").
    private static readonly IReadOnlyList<string> Models = [""];

    private sealed record DetectCache(CliBackendStatusDto Status, DateTime CheckedAt);
}

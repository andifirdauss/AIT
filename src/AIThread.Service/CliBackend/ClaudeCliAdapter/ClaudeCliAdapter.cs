using AIThread.Common.DTOs;
using AIThread.Common.Options;
using AIThread.Service.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AIThread.Service.CliBackend;

public sealed partial class ClaudeCliAdapter(IOptions<AIThreadOptions> options, ILogger<ClaudeCliAdapter> logger) : IClaudeCliAdapter
{
    public const string DisplayName = "Claude Code";
    private const string Command = "claude";
    private const string NpmPackage = "@anthropic-ai/claude-code";

    private static readonly TimeSpan DetectCacheDuration = TimeSpan.FromMinutes(5);

    private readonly SemaphoreSlim _slots = new(Math.Max(1, options.Value.ClaudeCli.MaxProcesses));
    private DetectCache? _detected;

    public string Tool => CliModelId.Claude;

    public string Name => DisplayName;

    // Claude Code's model aliases: each follows the provider's newest model of that family.
    private static readonly IReadOnlyList<string> Models = ["opus", "sonnet", "haiku", "fable"];

    private sealed record DetectCache(CliBackendStatusDto Status, DateTime CheckedAt);
}

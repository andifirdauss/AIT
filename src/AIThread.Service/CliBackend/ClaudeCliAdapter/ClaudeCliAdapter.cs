using System.Text;
using AIThread.Common.DTOs;
using AIThread.Common.Options;
using AIThread.Service.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AIThread.Service.CliBackend;

public sealed partial class ClaudeCliAdapter(IOptions<AIThreadOptions> options, ILogger<ClaudeCliAdapter> logger) : IClaudeCliAdapter
{
    public const string DisplayName = "Claude Code";

    private static readonly TimeSpan DetectCacheDuration = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan VersionTimeout = TimeSpan.FromSeconds(20);
    private static readonly UTF8Encoding Utf8 = new(false);

    // A fresh, empty folder per call: no CLAUDE.md, no .claude/ settings, nothing of the project for the CLI to read.
    private static readonly string WorkRoot =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AIThread", "cli-work");

    private readonly SemaphoreSlim _slots = new(Math.Max(1, options.Value.ClaudeCli.MaxProcesses));
    private DetectCache? _detected;

    // How to start claude: the executable and the arguments before AIT's own (cli.js for an npm install run by node).
    private sealed record Launch(string FileName, IReadOnlyList<string> Prefix)
    {
        public string Display => Prefix.Count == 0 ? FileName : Prefix[^1];
    }

    private sealed record DetectCache(CliBackendStatusDto Status, DateTime CheckedAt);
}

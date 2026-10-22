using AIThread.Common.DTOs;
using AIThread.Service.Interfaces;

namespace AIThread.Service.Implementations.Cli;

// Claude Code: the env block of settings.json / .claude/settings.local.json, written by ClaudeSettingsService.
public sealed partial class ClaudeCodeIntegration(IClaudeSettingsService claude) : ICliIntegration
{
    public const string Id = "claude-code";

    public CliInfoDto Info { get; } = new(
        Id,
        "Claude Code",
        "Menulis blok env (ANTHROPIC_BASE_URL, ANTHROPIC_AUTH_TOKEN) dan aturan permissions.deny untuk file kredensial di settings Claude Code.",
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "settings.json"),
        Path.Combine(".claude", "settings.local.json"),
        "Buka sesi Claude Code baru agar berlaku.");
}

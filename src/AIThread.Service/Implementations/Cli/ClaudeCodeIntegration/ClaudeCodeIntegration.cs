using AIThread.Common.DTOs;
using AIThread.Common.Localization;
using AIThread.Service.Interfaces;

namespace AIThread.Service.Implementations.Cli;

// Claude Code: the env block of settings.json / .claude/settings.local.json, written by ClaudeSettingsService.
public sealed partial class ClaudeCodeIntegration(IClaudeSettingsService claude) : ICliIntegration
{
    public const string Id = "claude-code";

    public CliInfoDto Info => new(
        Id,
        "Claude Code",
        Lang.T("Menulis blok env (ANTHROPIC_BASE_URL, ANTHROPIC_AUTH_TOKEN) dan aturan permissions.deny untuk file kredensial di settings Claude Code.", "Writes an env block (ANTHROPIC_BASE_URL, ANTHROPIC_AUTH_TOKEN) and permissions.deny rules for credential files in the Claude Code settings."),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "settings.json"),
        Path.Combine(".claude", "settings.local.json"),
        Lang.T("Buka sesi Claude Code baru agar berlaku.", "Open a new Claude Code session for it to take effect."));
}

using AiThread.Common.DTOs;
using AiThread.Common.Enums;
using AiThread.Service.Interfaces;

namespace AiThread.Service.Implementations.Cli;

// Claude Code: the env block of settings.json / .claude/settings.local.json, written by ClaudeSettingsService.
public sealed class ClaudeCodeIntegration(IClaudeSettingsService claude) : ICliIntegration
{
    public const string Id = "claude-code";

    public CliInfoDto Info { get; } = new(
        Id,
        "Claude Code",
        "Menulis blok env (ANTHROPIC_BASE_URL, ANTHROPIC_AUTH_TOKEN) di settings Claude Code.",
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "settings.json"),
        Path.Combine(".claude", "settings.local.json"),
        "Buka sesi Claude Code baru agar berlaku.");

    public async Task<CliStatusDto> GetStatusAsync(CliScope scope, Guid? projectId, string proxyBaseUrl, CancellationToken cancellationToken = default) =>
        ToStatus(scope, await claude.GetStatusAsync(ToClaudeScope(scope), projectId, proxyBaseUrl, cancellationToken));

    public async Task<CliStatusDto> ApplyAsync(CliScope scope, Guid? projectId, string proxyBaseUrl, CancellationToken cancellationToken = default) =>
        ToStatus(scope, await claude.ApplyAsync(new ClaudeSettingsApplyRequest(ToClaudeScope(scope), projectId, proxyBaseUrl, null), cancellationToken));

    public async Task<CliStatusDto> RevertAsync(CliScope scope, Guid? projectId, string proxyBaseUrl, CancellationToken cancellationToken = default) =>
        ToStatus(scope, await claude.RevertAsync(ToClaudeScope(scope), projectId, proxyBaseUrl, cancellationToken));

    public async Task<IReadOnlyList<ProjectCliStatusDto>> GetProjectStatusesAsync(string proxyBaseUrl, CancellationToken cancellationToken = default) =>
        (await claude.GetProjectStatusesAsync(proxyBaseUrl, cancellationToken))
        .Select(s => new ProjectCliStatusDto(s.ProjectId, s.Enabled, s.CurrentBaseUrl, s.Path, s.Error))
        .ToList();

    private static ClaudeSettingsScope ToClaudeScope(CliScope scope) =>
        scope == CliScope.Global ? ClaudeSettingsScope.User : ClaudeSettingsScope.Project;

    private static CliStatusDto ToStatus(CliScope scope, ClaudeSettingsStatusDto s) =>
        new(Id, scope, s.Path, s.FileExists, s.PointsToAiThread, s.CurrentBaseUrl, s.PreviewJson);
}

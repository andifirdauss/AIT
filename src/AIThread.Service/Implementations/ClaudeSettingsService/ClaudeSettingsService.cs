using System.Text.Json;
using AIThread.Service.Interfaces;
using AIThread.Service.Proxy;

namespace AIThread.Service.Implementations;

// Claude Code reads env vars from the "env" block of settings.json:
//   ANTHROPIC_BASE_URL   -> requests go to this proxy instead of api.anthropic.com
//   ANTHROPIC_AUTH_TOKEN -> sent as "Authorization: Bearer"; also keeps real Anthropic credentials away from the proxy
//   API_TIMEOUT_MS       -> an orchestrated thread can take minutes
//   ANTHROPIC_CUSTOM_HEADERS (project scope only) -> pins the session to its project root folder
public sealed partial class ClaudeSettingsService(ISettingService settingService, IProjectService projectService) : IClaudeSettingsService
{
    private const string ProjectHeader = ClaudeRequestInspector.ProjectHeader;
    private const int DefaultTimeoutMs = 1_800_000;
    private const string BackupSuffix = ".aithread-backup";

    private static readonly string[] ManagedKeys = ["ANTHROPIC_BASE_URL", "ANTHROPIC_AUTH_TOKEN", "API_TIMEOUT_MS"];
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };
    private static readonly JsonDocumentOptions ReadOptions = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
}

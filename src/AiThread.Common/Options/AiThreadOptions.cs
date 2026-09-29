namespace AiThread.Common.Options;

// Infrastructure-level knobs from appsettings.json (section "AiThread").
// Runtime settings editable from the UI live in SystemSetting instead.
public class AiThreadOptions
{
    public const string SectionName = "AiThread";

    public int ShortMemoryHours { get; set; } = 24;
    public int MemoryConsolidationIntervalMinutes { get; set; } = 10;
    public int RouterMonitorIntervalSeconds { get; set; } = 15;
    public int ActivityLogRetentionDays { get; set; } = 14;

    // Files up to this many lines are sent to executors in full; larger files are sent as excerpts.
    public int ExecutorFullFileMaxLines { get; set; } = 400;

    // Base URL the planner CLI uses to reach AiThread's MCP endpoint. Empty = the first http:// address Kestrel listens on.
    public string? LocalBaseUrl { get; set; }

    public ClaudeCliOptions ClaudeCli { get; set; } = new();
}

// Claude Code CLI used by planner nodes with the ClaudeCli backend.
public class ClaudeCliOptions
{
    // Executable name on PATH or a full path (claude.exe from the native installer, claude.cmd from npm).
    public string Command { get; set; } = "claude";

    // --setting-sources for the child CLI. User and local settings are skipped on purpose: that is where AiThread
    // writes ANTHROPIC_BASE_URL, which would route the planner back into this proxy.
    public string SettingSources { get; set; } = "project";

    // Hard limit for one planning run, including every tool call.
    public int TimeoutMinutes { get; set; } = 15;

    // Extra arguments appended as-is, e.g. ["--fallback-model", "sonnet"].
    public string[] ExtraArguments { get; set; } = [];
}

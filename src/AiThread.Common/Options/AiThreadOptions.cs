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
}

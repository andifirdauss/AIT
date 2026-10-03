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

    // Offline skill store (one folder per skill source). Empty = %LOCALAPPDATA%\AiThread\skills.
    public string? SkillsDirectory { get; set; }

    public TelegramOptions Telegram { get; set; } = new();

    public EmbeddedRedisOptions EmbeddedRedis { get; set; } = new();
}

// Short memory server (Microsoft Garnet, Redis protocol) hosted inside AiThread, so no Redis/Docker install is needed.
// It listens on the loopback port from ConnectionStrings:Redis.
public class EmbeddedRedisOptions
{
    // false = use an external Redis at ConnectionStrings:Redis instead.
    public bool Enabled { get; set; } = true;

    // Append-only file and checkpoints. Empty = %LOCALAPPDATA%\AiThread\garnet\<port>.
    public string? DataDirectory { get; set; }

    // RAM for the record log. The oldest records beyond it are dropped, so it must stay far above one short-memory
    // window of writes (a record is ~1 KB).
    public string MemorySize { get; set; } = "16m";

    // A checkpoint trims the append-only file; without it the file grows with every write ever made.
    public int CheckpointIntervalMinutes { get; set; } = 60;
}

// Telegram Bot API used for remote prompting. The bot token itself is a runtime setting (menu Setting).
public class TelegramOptions
{
    // Official endpoint by default; point it at a self-hosted Bot API server or a proxy when needed.
    public string ApiBaseUrl { get; set; } = "https://api.telegram.org";

    // How long one getUpdates call waits for new messages.
    public int PollTimeoutSeconds { get; set; } = 50;
}

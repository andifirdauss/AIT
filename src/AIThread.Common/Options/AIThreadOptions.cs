namespace AIThread.Common.Options;

// Infrastructure-level knobs from appsettings.json (section "AIThread").
// Runtime settings editable from the UI live in SystemSetting instead.
public class AIThreadOptions
{
    public const string SectionName = "AIThread";

    public int ShortMemoryHours { get; set; } = 24;
    public int MemoryConsolidationIntervalMinutes { get; set; } = 10;

    // How often MemoryRelabelWorker looks for memory without the structured label (older versions sharing a store
    // keep writing it).
    public int MemoryRelabelIntervalMinutes { get; set; } = 10;

    // Acronyms written in capitals in memory labels, on top of the built-in list (API, DTO, UI, ...).
    public string[] MemoryAcronyms { get; set; } = [];

    public int RouterMonitorIntervalSeconds { get; set; } = 15;
    public int ActivityLogRetentionDays { get; set; } = 14;

    // Files up to this many lines are sent to executors in full; larger files are sent as excerpts.
    public int ExecutorFullFileMaxLines { get; set; } = 400;

    // Offline skill store (one folder per skill source). Empty = %LOCALAPPDATA%\AIThread\skills.
    public string? SkillsDirectory { get; set; }

    // Key of the secret placeholders (HMAC, encrypted with Data Protection), so a secret keeps its placeholder across
    // restarts. Empty = %LOCALAPPDATA%\AIThread\secret-mask.key.
    public string? SecretKeyPath { get; set; }

    public TelegramOptions Telegram { get; set; } = new();

    public EmbeddedRedisOptions EmbeddedRedis { get; set; } = new();

    public NineRouterLaunchOptions NineRouter { get; set; } = new();
}

// Starting (and installing) 9router for the user. Only applies when the 9router URL in Setting is a local address.
public class NineRouterLaunchOptions
{
    // Start 9router when AIThread starts and finds it offline.
    public bool AutoStart { get; set; } = true;

    // Run `npm install -g 9router` when no 9router is found at startup (global npm, or the copy installed with the
    // aithread npm package). The dashboard's "Jalankan 9router" button always may install.
    public bool AutoInstall { get; set; }

    // Stop the 9router that AIThread started when AIThread stops. A 9router started by the user is never stopped.
    public bool StopOnExit { get; set; }

    // The first start also prepares 9router's SQLite runtime, which can download packages.
    public int StartTimeoutSeconds { get; set; } = 90;

    public int InstallTimeoutSeconds { get; set; } = 300;

    // Overrides; empty = search PATH, the global npm folder and the aithread npm package.
    public string? NodePath { get; set; }

    public string? CliPath { get; set; }
}

// Short memory server (Microsoft Garnet, Redis protocol) hosted inside AIThread, so no Redis/Docker install is needed.
// It listens on the loopback port from ConnectionStrings:Redis.
public class EmbeddedRedisOptions
{
    // false = use an external Redis at ConnectionStrings:Redis instead.
    public bool Enabled { get; set; } = true;

    // Append-only file and checkpoints. Empty = %LOCALAPPDATA%\AIThread\garnet\<port>.
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

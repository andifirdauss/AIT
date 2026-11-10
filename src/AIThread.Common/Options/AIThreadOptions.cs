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

    // A change of at most this many tasks that passed every check ends with the plan summary, without a wrapper call.
    // 0 = always call the wrapper.
    public int WrapperSkipMaxTasks { get; set; } = 2;

    // How long the final answer waits for the memory extraction, so its footer can name the new memories. 0: no wait; the
    // extraction goes on in the background either way and the dashboard and the tray show a notification when it ends.
    public int MemoryWaitSeconds { get; set; }

    // Offline skill store (one folder per skill source). Empty = %LOCALAPPDATA%\AIThread\skills.
    public string? SkillsDirectory { get; set; }

    // Key of the secret placeholders (HMAC, encrypted with Data Protection), so a secret keeps its placeholder across
    // restarts. Empty = %LOCALAPPDATA%\AIThread\secret-mask.key.
    public string? SecretKeyPath { get; set; }

    // Incidents (errors with their stack trace and companion files) for the Log menu. Empty = %LOCALAPPDATA%\AIThread\diagnostics.
    public string? DiagnosticsDirectory { get; set; }

    // Run logs of AIThread.exe (menu/tray mode). Empty = %LOCALAPPDATA%\AIThread\logs.
    public string? LogDirectory { get; set; }

    public TelegramOptions Telegram { get; set; } = new();

    public EmbeddedRedisOptions EmbeddedRedis { get; set; } = new();

    public NineRouterLaunchOptions NineRouter { get; set; } = new();

    public ClaudeCliOptions ClaudeCli { get; set; } = new();

    public CodexCliOptions CodexCli { get; set; } = new();

    public GeminiCliOptions GeminiCli { get; set; } = new();

    public AntigravityCliOptions AntigravityCli { get; set; } = new();

    public ProjectDatabaseOptions Databases { get; set; } = new();
}

// Project databases (Project > Detail → Database): limits of query_database and change_database.
public class ProjectDatabaseOptions
{
    // A query or a change that runs longer is cancelled.
    public int CommandTimeoutSeconds { get; set; } = 30;

    // A backup has no row limit, only the disk: a backup, comparison file or rehearsal copy stops (and nothing runs)
    // before its disk would keep less than this many MB free.
    public int BackupFreeSpaceReserveMb { get; set; } = 512;

    // Backups older than this are deleted (file and record); 0 keeps them.
    public int BackupRetentionDays { get; set; } = 30;

    // Backups and rehearsal copies. Empty = %LOCALAPPDATA%\AIThread\db-backups and \db-rehearsal.
    public string? Directory { get; set; }
}

// An official coding CLI run as a model engine for nodes whose model is "cli:<tool>:<model>" (CliBackend): AIT runs
// it headless with the login already on this machine, built-in tools off.
public class CliEngineOptions
{
    // The executable, the npm shim (.cmd) or the package's JS entry. Empty = PATH, then the usual install folders.
    public string? Path { get; set; }

    // Processes of this CLI running at once on this machine, over all nodes.
    public int MaxProcesses { get; set; } = 4;
}

// Claude Code: `claude -p` ("cli:claude:<model>").
public class ClaudeCliOptions : CliEngineOptions
{
    // --setting-sources of every run. "local" (in an empty working folder) skips the user's ~/.claude/settings.json and
    // CLAUDE.md: their hooks and an ANTHROPIC_BASE_URL pointing back at AIT must not apply. Managed policy always does.
    public string SettingSources { get; set; } = "local";

    // CLI processes kept started ahead of their call, one per model and system prompt, so a call does not wait for the
    // CLI to start. They use memory while they wait, for at most 90 seconds. 0 = start each process when it is needed.
    public int WarmProcesses { get; set; } = 4;
}

// Codex CLI: `codex exec` ("cli:codex:<model>").
public class CodexCliOptions : CliEngineOptions
{
    // --ignore-user-config: skip $CODEX_HOME/config.toml (its MCP servers, profiles, and a provider pointing back at
    // AIT). The login (auth.json) and managed config still apply. Turn off when the company provider is in config.toml.
    public bool IgnoreUserConfig { get; set; } = true;
}

// Gemini CLI: `gemini -p` ("cli:gemini:<model>").
public class GeminiCliOptions : CliEngineOptions;

// Antigravity CLI: `agy -p` ("cli:agy:<model>", the models `agy models` lists).
public class AntigravityCliOptions : CliEngineOptions
{
    // agy saves every run as a conversation (~/.gemini/antigravity-cli/conversations/<id>.db and brain/<id>/, a few
    // hundred KB each, dozens per thread). AIT deletes the ones its own calls made, right after each call.
    public bool DeleteConversations { get; set; } = true;
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

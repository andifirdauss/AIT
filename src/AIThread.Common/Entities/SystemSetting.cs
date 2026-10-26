namespace AIThread.Common.Entities;

// Single-row table holding the runtime configuration edited from the Setting menu.
public class SystemSetting : BaseEntity
{
    public static readonly Guid SingletonId = new("7c1e6d2a-5b1f-4c3e-9a47-000000000001");

    public string NineRouterBaseUrl { get; set; } = "http://localhost:20128";

    // Encrypted with ASP.NET Core Data Protection; never exposed through the API.
    public string? NineRouterApiKeyProtected { get; set; }

    // Key every CLI must send as bearer token (Claude Code: ANTHROPIC_AUTH_TOKEN). Empty = accept any caller.
    public string? ProxyApiKey { get; set; }

    // 9router model used for CLI requests that are not orchestrated (title generation, compact, etc.).
    public string? PassthroughModel { get; set; }

    public bool OrchestrationEnabled { get; set; } = true;
    public bool StreamProgressToClient { get; set; } = true;
    public int PlannerMaxSteps { get; set; } = 24;
    public int ExecutorMaxRetries { get; set; } = 1;
    public int MaxParallelTasks { get; set; } = 8;
    public int RequestTimeoutSeconds { get; set; } = 600;

    // The planner may pause a thread with a clarifying question (ask_user) when the request is genuinely ambiguous.
    public bool PlannerCanAsk { get; set; } = true;

    // AIThread may run the project's own build command (dotnet build, npm run build, ...): as a planner tool and to
    // verify the executors' changes, with up to MaxFixRounds rounds of fix tasks for the errors found.
    public bool BuildEnabled { get; set; } = true;
    public int BuildTimeoutSeconds { get; set; } = 300;
    public int MaxFixRounds { get; set; } = 2;

    // Remote prompting through a Telegram bot. Long polling, so no public URL is needed.
    public bool TelegramEnabled { get; set; }

    // Encrypted like the 9router key; never exposed through the API.
    public string? TelegramBotTokenProtected { get; set; }

    // Where short memory (default: embedded Garnet) and long memory (default: this database) are kept.
    public MemoryStoreSetting ShortMemoryStore { get; set; } = new();
    public MemoryStoreSetting LongMemoryStore { get; set; } = new();

    // Where the business wiki (business memory) is kept; share it to build one wiki from every developer's work.
    public MemoryStoreSetting BusinessMemoryStore { get; set; } = new();

    // 9router model the skill orchestrator uses to analyse skill sources and suggest skills for a node.
    // Null = the model of the first active Planner node.
    public string? SkillOrchestratorModel { get; set; }

    // Budget for the skill contents put in one node's system prompt.
    public int SkillPromptMaxChars { get; set; } = 40_000;

    // Business memory: the finished threads are summarized into the business wiki every BusinessMemoryIntervalHours.
    public bool BusinessMemoryEnabled { get; set; } = true;
    public int BusinessMemoryIntervalHours { get; set; } = 6;

    // 9router model of the business wiki writer. Null = the model of the first active Planner node.
    public string? BusinessMemoryModel { get; set; }

    // Name recorded on business pages and revisions. Null = the Windows user name.
    public string? DeveloperName { get; set; }

    // Secrets (passwords, API keys, tokens, private keys, ...) are replaced by placeholders before any text reaches a
    // model, and put back only when AIT writes a file (SecretGuard). PassthroughSecretMasking also masks the requests
    // forwarded to 9router without orchestration.
    public bool SecretMaskingEnabled { get; set; } = true;
    public bool PassthroughSecretMasking { get; set; } = true;

    // Language of the text AIT shows its user (Lang): "id" (default) or "en".
    public string UiLanguage { get; set; } = "id";

    // When the security & privacy notice was closed; null = it opens on the next page load.
    public DateTime? PrivacyNoticeAcknowledgedAt { get; set; }
}

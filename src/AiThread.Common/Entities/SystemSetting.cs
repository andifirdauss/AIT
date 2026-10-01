namespace AiThread.Common.Entities;

// Single-row table holding the runtime configuration edited from the Setting menu.
public class SystemSetting : BaseEntity
{
    public static readonly Guid SingletonId = new("7c1e6d2a-5b1f-4c3e-9a47-000000000001");

    public string NineRouterBaseUrl { get; set; } = "http://localhost:20128";

    // Encrypted with ASP.NET Core Data Protection; never exposed through the API.
    public string? NineRouterApiKeyProtected { get; set; }

    // Key Claude Code must send (ANTHROPIC_AUTH_TOKEN). Empty = accept any caller.
    public string? ProxyApiKey { get; set; }

    // 9router model used for Claude Code requests that are not orchestrated (title generation, compact, etc.).
    public string? PassthroughModel { get; set; }

    public bool OrchestrationEnabled { get; set; } = true;
    public bool StreamProgressToClient { get; set; } = true;
    public int PlannerMaxSteps { get; set; } = 24;
    public int ExecutorMaxRetries { get; set; } = 1;
    public int MaxParallelTasks { get; set; } = 8;
    public int RequestTimeoutSeconds { get; set; } = 600;

    // AiThread may run the project's own build command (dotnet build, npm run build, ...): as a planner tool and to
    // verify the executors' changes, with up to MaxFixRounds rounds of fix tasks for the errors found.
    public bool BuildEnabled { get; set; } = true;
    public int BuildTimeoutSeconds { get; set; } = 300;
    public int MaxFixRounds { get; set; } = 2;

    // Remote prompting through a Telegram bot. Long polling, so no public URL is needed.
    public bool TelegramEnabled { get; set; }

    // Encrypted like the 9router key; never exposed through the API.
    public string? TelegramBotTokenProtected { get; set; }
}

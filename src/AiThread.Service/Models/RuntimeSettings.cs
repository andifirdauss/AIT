namespace AiThread.Service.Models;

// Decrypted, cached snapshot of SystemSetting used on hot paths (proxy, orchestrator).
public sealed record RuntimeSettings(
    string NineRouterBaseUrl,
    string? NineRouterApiKey,
    string? ProxyApiKey,
    string? PassthroughModel,
    bool OrchestrationEnabled,
    bool StreamProgressToClient,
    int PlannerMaxSteps,
    int ExecutorMaxRetries,
    int MaxParallelTasks,
    int RequestTimeoutSeconds,
    bool TelegramEnabled = false,
    string? TelegramBotToken = null,
    bool BuildEnabled = true,
    int BuildTimeoutSeconds = 300,
    int MaxFixRounds = 2);

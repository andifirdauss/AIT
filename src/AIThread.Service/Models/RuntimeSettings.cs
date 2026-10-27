using AIThread.Service.Workspace;

namespace AIThread.Service.Models;

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
    int MaxFixRounds = 2,
    bool PlannerCanAsk = true,
    string? SkillOrchestratorModel = null,
    int SkillPromptMaxChars = 40_000,
    bool BusinessMemoryEnabled = true,
    int BusinessMemoryIntervalHours = 6,
    string? BusinessMemoryModel = null,
    string? DeveloperName = null,
    bool CommandToolEnabled = true,
    int CommandTimeoutSeconds = 60,
    bool GitPublishEnabled = true)
{
    // Recorded on business pages and revisions so the team sees who (or whose AIThread) wrote what: the name saved in
    // the Setting menu, else the global git user.name, else the Windows user.
    public string EffectiveDeveloperName =>
        !string.IsNullOrWhiteSpace(DeveloperName) ? DeveloperName : GitIdentity.UserName ?? Environment.UserName;
}

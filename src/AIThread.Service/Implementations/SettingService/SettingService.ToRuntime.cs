using AIThread.Common.Entities;
using AIThread.Service.Models;

namespace AIThread.Service.Implementations;

public sealed partial class SettingService
{
    private RuntimeSettings ToRuntime(SystemSetting entity) => new(
        entity.NineRouterBaseUrl,
        Unprotect(entity.NineRouterApiKeyProtected),
        entity.ProxyApiKey,
        entity.PassthroughModel,
        entity.OrchestrationEnabled,
        entity.StreamProgressToClient,
        entity.PlannerMaxSteps,
        entity.ExecutorMaxRetries,
        entity.MaxParallelTasks,
        entity.RequestTimeoutSeconds,
        entity.TelegramEnabled,
        Unprotect(entity.TelegramBotTokenProtected),
        entity.BuildEnabled,
        entity.BuildTimeoutSeconds,
        entity.MaxFixRounds,
        entity.PlannerCanAsk,
        entity.SkillOrchestratorModel,
        entity.SkillPromptMaxChars,
        entity.BusinessMemoryEnabled,
        entity.BusinessMemoryIntervalHours,
        entity.BusinessMemoryModel,
        entity.DeveloperName);
}

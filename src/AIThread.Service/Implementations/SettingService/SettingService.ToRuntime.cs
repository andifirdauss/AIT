using AIThread.Common.Entities;
using AIThread.Common.Localization;
using AIThread.Service.Models;

namespace AIThread.Service.Implementations;

public sealed partial class SettingService
{
    // Every load and save of the settings passes here, so the secret guard follows the stored switches too.
    private RuntimeSettings ToRuntime(SystemSetting entity)
    {
        secrets.Configure(entity.SecretMaskingEnabled, entity.PassthroughSecretMasking);
        Lang.Set(entity.UiLanguage);
        return new RuntimeSettings(
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
}

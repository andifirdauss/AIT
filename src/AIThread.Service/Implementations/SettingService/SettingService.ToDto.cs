using AIThread.Common.DTOs;
using AIThread.Common.Entities;
using AIThread.Common.Localization;

namespace AIThread.Service.Implementations;

public sealed partial class SettingService
{
    private SystemSettingDto ToDto(SystemSetting entity, string proxyBaseUrl)
    {
        var key = Unprotect(entity.NineRouterApiKeyProtected);
        return new SystemSettingDto(
            entity.NineRouterBaseUrl,
            key is not null,
            Mask(key),
            entity.ProxyApiKey,
            entity.PassthroughModel,
            entity.OrchestrationEnabled,
            entity.StreamProgressToClient,
            entity.PlannerMaxSteps,
            entity.ExecutorMaxRetries,
            entity.MaxParallelTasks,
            entity.RequestTimeoutSeconds,
            entity.BuildEnabled,
            entity.BuildTimeoutSeconds,
            entity.MaxFixRounds,
            proxyBaseUrl,
            entity.PlannerCanAsk,
            Lang.Normalize(entity.UiLanguage),
            entity.CommandToolEnabled,
            entity.CommandTimeoutSeconds,
            entity.GitPublishEnabled,
            entity.WebFetchEnabled);
    }
}

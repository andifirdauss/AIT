using AiThread.Common.Enums;

namespace AiThread.Common.DTOs;

public record SystemSettingDto(
    string NineRouterBaseUrl,
    bool HasNineRouterApiKey,
    string? NineRouterApiKeyMasked,
    string? ProxyApiKey,
    string? PassthroughModel,
    bool OrchestrationEnabled,
    bool StreamProgressToClient,
    int PlannerMaxSteps,
    int ExecutorMaxRetries,
    int MaxParallelTasks,
    int RequestTimeoutSeconds,
    string ProxyBaseUrl);

public record UpdateSystemSettingRequest(
    string NineRouterBaseUrl,
    string? NineRouterApiKey,
    bool ClearNineRouterApiKey,
    string? ProxyApiKey,
    string? PassthroughModel,
    bool OrchestrationEnabled,
    bool StreamProgressToClient,
    int PlannerMaxSteps,
    int ExecutorMaxRetries,
    int MaxParallelTasks,
    int RequestTimeoutSeconds);

public record ClaudeSettingsStatusDto(
    ClaudeSettingsScope Scope,
    string Path,
    bool FileExists,
    bool PointsToAiThread,
    string? CurrentBaseUrl,
    string PreviewJson);

public record ClaudeSettingsApplyRequest(
    ClaudeSettingsScope Scope,
    Guid? ProjectId,
    string ProxyBaseUrl,
    int? TimeoutMs);

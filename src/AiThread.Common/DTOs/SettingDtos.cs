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

// Enabled = the project's settings.local.json points Claude Code at this proxy. CurrentBaseUrl is set when it points
// somewhere else (e.g. an old port); Error when the file cannot be read.
public record ProjectClaudeIntegrationDto(Guid ProjectId, bool Enabled, string? CurrentBaseUrl, string Path, string? Error);

public record SetProjectClaudeIntegrationRequest(bool Enabled);

public record ClaudeSettingsApplyRequest(
    ClaudeSettingsScope Scope,
    Guid? ProjectId,
    string ProxyBaseUrl,
    int? TimeoutMs);

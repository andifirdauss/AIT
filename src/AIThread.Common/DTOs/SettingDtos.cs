using AIThread.Common.Enums;

namespace AIThread.Common.DTOs;

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
    bool BuildEnabled,
    int BuildTimeoutSeconds,
    int MaxFixRounds,
    string ProxyBaseUrl,
    bool PlannerCanAsk = true,
    string UiLanguage = "id",
    bool CommandToolEnabled = true,
    int CommandTimeoutSeconds = 60,
    bool GitPublishEnabled = true,
    bool WebFetchEnabled = true);

public record UpdateLanguageRequest(string Language);

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
    int RequestTimeoutSeconds,
    bool BuildEnabled = true,
    int BuildTimeoutSeconds = 300,
    int MaxFixRounds = 2,
    bool PlannerCanAsk = true,
    bool CommandToolEnabled = true,
    int CommandTimeoutSeconds = 60,
    bool GitPublishEnabled = true,
    bool WebFetchEnabled = true);

public record ClaudeSettingsStatusDto(
    ClaudeSettingsScope Scope,
    string Path,
    bool FileExists,
    bool PointsToAIThread,
    string? CurrentBaseUrl,
    string PreviewJson,
    bool SecretRulesApplied = false);

// Enabled = the project's settings.local.json points Claude Code at this proxy. CurrentBaseUrl is set when it points
// somewhere else (e.g. an old port); Error when the file cannot be read.
public record ProjectClaudeIntegrationDto(Guid ProjectId, bool Enabled, string? CurrentBaseUrl, string Path, string? Error);

// Installed = the /clear hooks point at this AIThread; InstalledBaseUrl is set when they exist at all.
public record ClaudeHooksStatusDto(
    ClaudeSettingsScope Scope,
    string Path,
    bool Installed,
    string? InstalledBaseUrl,
    string PreviewJson);

public record ClaudeSettingsApplyRequest(
    ClaudeSettingsScope Scope,
    Guid? ProjectId,
    string ProxyBaseUrl,
    int? TimeoutMs);

// The Setting tab "Keamanan & privasi" and the first-run notice. The counters are since AIT started: Masked counts every
// value replaced on its way to a model, KnownSecrets the distinct values AIT can write back, Restored the placeholders
// written back into files or tool calls, BlockedReads the reads of credential files refused.
public record SecurityStatusDto(
    bool MaskingEnabled,
    bool PassthroughMasking,
    DateTime? NoticeAcknowledgedAt,
    long Masked,
    int KnownSecrets,
    long Restored,
    long BlockedReads);

public record UpdateSecurityRequest(bool MaskingEnabled, bool PassthroughMasking);

// A value masked since AIT started (or marked as not secret earlier): its placeholder id and label, a preview that does
// not give the value away, and how often it was masked. Allowed: marked as not secret, so no longer masked.
public record DetectedSecretDto(
    string Id,
    string Label,
    string Preview,
    int Length,
    long Count,
    DateTime? FirstSeen,
    DateTime? LastSeen,
    bool Allowed,
    DateTime? AllowedAt);

public record DetectedSecretsDto(IReadOnlyList<DetectedSecretDto> Items, int Total);

public record SecretValueDto(string Id, string Value);

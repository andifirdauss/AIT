namespace AiThread.Common.DTOs;

public record RouterStatusDto(
    bool IsOnline,
    string BaseUrl,
    long? LatencyMs,
    int ModelCount,
    int ComboCount,
    bool HasApiKey,
    bool? ApiKeyValid,
    string? Error,
    DateTime CheckedAt);

public record RouterModelDto(
    string Id,
    string OwnedBy,
    bool IsCombo,
    int? ContextLength,
    int? MaxCompletionTokens);

public record TestRouterRequest(string? BaseUrl, string? ApiKey);

// Remaining quota of one 9router provider account (connection), from 9router's GET /api/usage/{connectionId}.
// Plan: 9router's label for the subscription (e.g. "Claude Code"); Name: the account name in the 9router dashboard.
public record RouterQuotaDto(
    string ConnectionId,
    string Provider,
    string Name,
    string Plan,
    IReadOnlyList<RouterQuotaWindowDto> Windows);

// One limit of an account: a time window ("session (5h)", "weekly (7d)") or a per-model quota. RemainingPercent is
// null when unlimited or unknown.
public record RouterQuotaWindowDto(
    string Name,
    double? RemainingPercent,
    bool Unlimited,
    DateTime? ResetAt);

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

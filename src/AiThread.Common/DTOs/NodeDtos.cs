using AiThread.Common.Enums;

namespace AiThread.Common.DTOs;

public record NodeDto(
    Guid Id,
    string Name,
    NodeRole Role,
    NodeBackend Backend,
    string Model,
    double? Temperature,
    int MaxTokens,
    int MaxConcurrency,
    int Priority,
    string? SystemPrompt,
    bool IsActive,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

public record SaveNodeRequest(
    string Name,
    NodeRole Role,
    string Model,
    double? Temperature,
    int MaxTokens,
    int MaxConcurrency,
    int Priority,
    string? SystemPrompt,
    bool IsActive,
    NodeBackend Backend = NodeBackend.Router);

public record PipelineStatusDto(
    bool IsReady,
    int PlannerCount,
    int ExecutorCount,
    int WrapperCount,
    IReadOnlyList<string> Issues);

public record NodeTestResultDto(bool Success, string? Reply, long LatencyMs, int Tokens, string? Error);

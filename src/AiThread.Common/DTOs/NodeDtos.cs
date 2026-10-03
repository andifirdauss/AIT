using AiThread.Common.Enums;

namespace AiThread.Common.DTOs;

public record NodeDto(
    Guid Id,
    string Name,
    NodeRole Role,
    string Model,
    double? Temperature,
    int MaxTokens,
    int MaxConcurrency,
    int Priority,
    string? SystemPrompt,
    bool IsActive,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    IReadOnlyList<NodeSkillDto>? Skills = null);

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
    IReadOnlyList<Guid>? SkillIds = null);

public record PipelineStatusDto(
    bool IsReady,
    int PlannerCount,
    int ExecutorCount,
    int WrapperCount,
    IReadOnlyList<string> Issues);

public record NodeTestResultDto(bool Success, string? Reply, long LatencyMs, int Tokens, string? Error);

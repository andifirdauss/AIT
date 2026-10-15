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
    IReadOnlyList<NodeSkillDto>? Skills = null,
    Guid? FallbackNodeId = null,
    string? FallbackNodeName = null,
    IReadOnlyList<ToolDelegationDto>? ToolDelegations = null);

// A planner tool run by a tools node instead of the planner itself.
public record ToolDelegationDto(string Tool, Guid ToolsNodeId, string ToolsNodeName, bool ToolsNodeActive);

public record ToolDelegationRequest(string Tool, Guid ToolsNodeId);

// FallbackNodeId: a node of the same role that takes over when this one is unavailable; null = none.
// ToolDelegations (planners only): null = leave as they are (clients that do not know about tools nodes).
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
    IReadOnlyList<Guid>? SkillIds = null,
    Guid? FallbackNodeId = null,
    IReadOnlyList<ToolDelegationRequest>? ToolDelegations = null);

public record PipelineStatusDto(
    bool IsReady,
    int PlannerCount,
    int ExecutorCount,
    int WrapperCount,
    IReadOnlyList<string> Issues);

public record NodeTestResultDto(bool Success, string? Reply, long LatencyMs, int Tokens, string? Error);

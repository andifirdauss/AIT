using AIThread.Common.Enums;

namespace AIThread.Common.DTOs;

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
    IReadOnlyList<ToolDelegationDto>? ToolDelegations = null,
    bool AutoTesting = false,
    bool DirectRead = false,
    int DirectReadPercent = 100);

// A planner tool run by a tools node instead of the planner itself.
public record ToolDelegationDto(string Tool, Guid ToolsNodeId, string ToolsNodeName, bool ToolsNodeActive);

public record ToolDelegationRequest(string Tool, Guid ToolsNodeId);

// FallbackNodeId: a node of the same role that takes over when this one is unavailable; null = none.
// ToolDelegations (planners only): null = leave as they are (clients that do not know about tools nodes).
// AutoTesting, DirectRead and DirectReadPercent (planners only): null = leave it as it is.
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
    IReadOnlyList<ToolDelegationRequest>? ToolDelegations = null,
    bool? AutoTesting = null,
    bool? DirectRead = null,
    int? DirectReadPercent = null);

public record PipelineStatusDto(
    bool IsReady,
    int PlannerCount,
    int ExecutorCount,
    int WrapperCount,
    IReadOnlyList<string> Issues);

public record NodeTestResultDto(bool Success, string? Reply, long LatencyMs, int Tokens, string? Error);

// One node in the benchmark of a small change. Calls: how many times a small change calls it; EstimatedMs: Calls times
// the median of its model (null when every sample failed); SharePercent: its part of the estimate.
public record NodeBenchmarkStageDto(
    NodeRole Role,
    Guid NodeId,
    string NodeName,
    string Model,
    int Calls,
    long? MedianMs,
    long? MinMs,
    long? MaxMs,
    long? EstimatedMs,
    int SharePercent,
    int Failed,
    string? Error,
    bool Unstable,
    bool IsBottleneck);

// SamplesPerModel: the calls made to each distinct model. BottleneckNode: null when nothing could be measured.
public record NodeBenchmarkDto(
    IReadOnlyList<NodeBenchmarkStageDto> Stages,
    long EstimatedMs,
    int SamplesPerModel,
    string? BottleneckNode,
    IReadOnlyList<string> Tips);

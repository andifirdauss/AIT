using AiThread.Common.Enums;

namespace AiThread.Common.DTOs;

public record ThreadSummaryDto(
    Guid Id,
    Guid? ProjectId,
    string? ProjectName,
    ThreadSource Source,
    string? SessionId,
    string Prompt,
    ThreadStatus Status,
    string? PlanSummary,
    int TaskCount,
    int TasksSucceeded,
    int TasksFailed,
    long? TotalMs,
    long? ExecutionWallMs,
    long? ExecutionSumMs,
    int TotalTokens,
    DateTime CreatedAt,
    DateTime? CompletedAt);

public record ThreadTaskDto(
    Guid Id,
    string TaskKey,
    int Order,
    string Title,
    string FilePath,
    string Action,
    string Instruction,
    string? DependsOn,
    ThreadTaskStatus Status,
    string? NodeName,
    string? Model,
    int Attempts,
    string? Summary,
    string? Diff,
    string? Error,
    int Tokens,
    DateTime? StartedAt,
    DateTime? CompletedAt,
    long? DurationMs);

public record ThreadDetailDto(
    ThreadSummaryDto Summary,
    string? PlanJson,
    string? FinalResponse,
    string? Error,
    string? PlannerModel,
    string? WrapperModel,
    int PlannerSteps,
    int PlannerTokens,
    int ExecutorTokens,
    int WrapperTokens,
    long? PlanningMs,
    long? WrappingMs,
    string? WorkingDirectory,
    IReadOnlyList<ThreadTaskDto> Tasks,
    string? ContextPack = null);

public record StartThreadRequest(Guid ProjectId, string Prompt);

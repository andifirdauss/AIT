using AIThread.Common.Enums;

namespace AIThread.Common.DTOs;

public record DashboardSummaryDto(
    RouterStatusDto? Router,
    bool RedisConnected,
    bool DatabaseConnected,
    PipelineStatusDto Pipeline,
    int ThreadsToday,
    int ThreadsTotal,
    int ActiveThreads,
    double SuccessRate,
    double AverageSpeedup,
    long TokensToday,
    int ProjectCount,
    MemoryStatsDto Memory);

public record ActivityLogDto(
    long Id,
    DateTime CreatedAt,
    ActivityLevel Level,
    string Category,
    string Message,
    Guid? ThreadId,
    Guid? ProjectId);

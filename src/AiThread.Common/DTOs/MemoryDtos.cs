using AiThread.Common.Enums;

namespace AiThread.Common.DTOs;

public record MemoryItemDto(
    Guid Id,
    MemoryLevel Level,
    MemoryType Type,
    Guid? ProjectId,
    string? ProjectName,
    string Label,
    string Description,
    Guid? SourceThreadId,
    DateTime CreatedAt,
    DateTime? PromotedAt,
    int HitCount);

// Label-only view used by the planner to pick topics without reading descriptions.
public record MemoryIndexEntry(Guid Id, MemoryLevel Level, MemoryType Type, string Label, DateTime CreatedAt);

public record MemoryQuery(
    MemoryLevel? Level,
    MemoryType? Type,
    Guid? ProjectId,
    string? Search,
    int Page = 1,
    int PageSize = 50);

public record SaveMemoryRequest(MemoryType Type, Guid? ProjectId, string Label, string Description);

// RedisConnected is the state of the short memory store, whatever its provider (name kept for API compatibility).
// ShortStore/LongStore name where each level is kept (menu Setting → Penyimpanan memory).
public record MemoryStatsDto(
    int ShortCount,
    int LongCount,
    int ProjectCount,
    int KnowledgeCount,
    bool RedisConnected,
    string? ShortStore = null,
    string? LongStore = null);

public record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);

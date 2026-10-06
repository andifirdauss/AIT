using AiThread.Common.Enums;

namespace AiThread.Data.Cache;

// Shape of a short-term memory item as stored in Redis. ProjectKey/ProjectName are missing from records written by
// older versions; MemoryService falls back to ProjectId for those.
public sealed record ShortMemoryRecord(
    Guid Id,
    Guid? ProjectId,
    MemoryType Type,
    string Label,
    string Description,
    Guid? SourceThreadId,
    DateTime CreatedAt,
    string? ProjectKey = null,
    string? ProjectName = null);

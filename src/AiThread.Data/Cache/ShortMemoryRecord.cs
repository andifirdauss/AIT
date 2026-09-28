using AiThread.Common.Enums;

namespace AiThread.Data.Cache;

// Shape of a short-term memory item as stored in Redis.
public sealed record ShortMemoryRecord(
    Guid Id,
    Guid? ProjectId,
    MemoryType Type,
    string Label,
    string Description,
    Guid? SourceThreadId,
    DateTime CreatedAt);

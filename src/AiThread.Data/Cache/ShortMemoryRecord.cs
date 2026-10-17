using AiThread.Common.Enums;

namespace AiThread.Data.Cache;

// Shape of a short-term memory item as stored in Redis. ProjectKey/ProjectName are missing from records written by
// older versions; MemoryService falls back to ProjectId for those. The label fields (Module … RelabelAttempts) mirror
// LongMemory; a record without LabelVersion is relabeled by MemoryRelabelWorker.
public sealed record ShortMemoryRecord(
    Guid Id,
    Guid? ProjectId,
    MemoryType Type,
    string Label,
    string Description,
    Guid? SourceThreadId,
    DateTime CreatedAt,
    string? ProjectKey = null,
    string? ProjectName = null,
    string? Module = null,
    string? Layers = null,
    MemoryKind? Kind = null,
    string? Area = null,
    string? Keywords = null,
    int? LabelVersion = null,
    string? LegacyLabel = null,
    string? ReviewReason = null,
    int? RelabelAttempts = null,
    string? ExtraModules = null);

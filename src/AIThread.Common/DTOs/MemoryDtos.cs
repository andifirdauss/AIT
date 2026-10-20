using AIThread.Common.Enums;
using AIThread.Common.Memory;

namespace AIThread.Common.DTOs;

// Label is the display label (MemoryLabels.Display) once labeled, the old free label before. Labeled is false for
// memory written by an older version that MemoryRelabelWorker has not relabeled yet.
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
    int HitCount,
    string? ProjectKey = null,
    string? Module = null,
    IReadOnlyList<MemoryLayer>? Layers = null,
    MemoryKind? Kind = null,
    string? Area = null,
    IReadOnlyList<string>? Keywords = null,
    bool Labeled = false,
    string? LegacyLabel = null,
    string? ReviewReason = null,
    IReadOnlyList<string>? ExtraModules = null);

// What the planner scans to pick topics without reading descriptions. Labeled entries carry the structured label;
// unlabeled ones (older memory waiting for relabeling) only Label.
public record MemoryIndexEntry(
    Guid Id,
    MemoryLevel Level,
    MemoryType Type,
    string Label,
    DateTime CreatedAt,
    string? Module = null,
    IReadOnlyList<MemoryLayer>? Layers = null,
    MemoryKind? Kind = null,
    string? Area = null,
    IReadOnlyList<string>? Keywords = null,
    int HitCount = 0,
    IReadOnlyList<string>? ExtraModules = null)
{
    public bool Labeled => Module is not null && Kind is not null;

    // Main module first, then the extra ones.
    public IReadOnlyList<string> Modules => MemoryLabels.AllModules(Module, ExtraModules);
}

public record MemoryQuery(
    MemoryLevel? Level,
    MemoryType? Type,
    Guid? ProjectId,
    string? Search,
    int Page = 1,
    int PageSize = 50,
    string? Module = null,
    MemoryLayer? Layer = null,
    MemoryKind? Kind = null,
    bool? Labeled = null,
    bool? NeedsReview = null);

// Every memory is written with its full label. Module (the main one) and ExtraModules (other modules the same fact
// concerns, at most 3) are resolved against the project's module list (a new name becomes a module); keywords are
// normalized to PascalCase, at most 16.
public record SaveMemoryRequest(
    MemoryType Type,
    Guid? ProjectId,
    string Module,
    MemoryKind Kind,
    IReadOnlyList<string> Keywords,
    string Description,
    IReadOnlyList<MemoryLayer>? Layers = null,
    string? Area = null,
    IReadOnlyList<string>? ExtraModules = null);

// The planner's two-layer search: Module (and optionally Layer/Kind) narrows the list, Keywords rank inside it.
// Text is free text (a prompt or an old-style query) used to detect modules and keywords when they are not given.
public record MemorySearch(
    Guid? ProjectId,
    string? Module = null,
    MemoryLayer? Layer = null,
    MemoryKind? Kind = null,
    IReadOnlyList<string>? Keywords = null,
    string? Text = null,
    int Take = 25,
    IReadOnlyList<string>? Modules = null);

// RedisConnected is the state of the short memory store, whatever its provider (name kept for API compatibility).
// ShortStore/LongStore name where each level is kept (menu Setting → Penyimpanan memory).
public record MemoryStatsDto(
    int ShortCount,
    int LongCount,
    int ProjectCount,
    int KnowledgeCount,
    bool RedisConnected,
    string? ShortStore = null,
    string? LongStore = null,
    int UnlabeledCount = 0,
    int ReviewCount = 0);

public record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);

// One line of the planner's memory map: how many labeled memories a module has, per layer and per kind.
public record MemoryModuleSummary(
    string Module,
    bool Knowledge,
    int Count,
    IReadOnlyDictionary<MemoryLayer, int> Layers,
    IReadOnlyDictionary<MemoryKind, int> Kinds);

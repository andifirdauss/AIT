using AIThread.Common.Enums;

namespace AIThread.Common.DTOs;

public record ProjectModuleDto(
    Guid Id,
    string Name,
    IReadOnlyList<string> Aliases,
    string? Description,
    int Memories,
    int Files);

// Name: a new name (or only another casing) for the module; null keeps it.
public record SaveModuleRequest(string? Description, IReadOnlyList<string>? Aliases, string? Name = null);

public record MergeModulesRequest(Guid ProjectId, string From, string Into);

public record ModuleMergeDto(
    Guid Id,
    string From,
    string Into,
    ModuleMergeStatus Status,
    string? Reason,
    double? Confidence,
    string? DecidedBy,
    DateTime CreatedAt,
    DateTime? DecidedAt,
    int Memories,
    int Files,
    int Pages);

public record CodeMapEntryDto(Guid Id, string Path, string Module, MemoryLayer Layer, string? Area, CodeMapSource Source, DateTime UpdatedAt);

// Path may also be a file or class name; it is resolved to a file of the project.
public record SetCodeMapRequest(string Path, string Module, MemoryLayer? Layer);

// Two memories that likely say the same thing: same module and kind, overlapping keywords.
public record MemoryDuplicateDto(MemoryItemDto First, MemoryItemDto Second, double Similarity);

public record MemoryReviewDto(
    IReadOnlyList<MemoryItemDto> Memories,
    IReadOnlyList<ModuleMergeDto> Merges,
    IReadOnlyList<MemoryDuplicateDto> Duplicates);

// Progress of the automatic relabeling of memory written by older versions. Detail: what the phase is working on
// (e.g. "KN2018_KCO (1/5) · 363/1.700 file").
public record MemoryRelabelStatusDto(
    bool Running,
    string? Phase,
    int Pending,
    int Labeled,
    int NeedsReview,
    DateTime? LastRunAt,
    string? LastMessage,
    string? BackupPath,
    string? Detail = null);

public record CodeMapScanResultDto(int Files, int Mapped, int Modules);

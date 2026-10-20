using AIThread.Common.Enums;

namespace AIThread.Common.DTOs;

// A portable export of short and long memory, independent of the store each level uses. Projects are named by
// ProjectKey (shared by every clone of a repository); ProjectId is only a hint for restoring on the same machine.
public record MemoryExportFile(
    string Format,
    int Version,
    DateTime ExportedAt,
    string? AppVersion,
    IReadOnlyList<MemoryExportItem> Items)
{
    public const string FormatName = "aithread-memory";

    // v2 adds the structured label; a v1 item (or a v2 item without Module/Kind) is imported unlabeled and relabeled
    // by MemoryRelabelWorker.
    public const int CurrentVersion = 2;
}

public record MemoryExportItem(
    Guid Id,
    MemoryLevel Level,
    MemoryType Type,
    Guid? ProjectId,
    string? ProjectKey,
    string? ProjectName,
    string Label,
    string Description,
    Guid? SourceThreadId,
    DateTime CreatedAt,
    DateTime? PromotedAt = null,
    int HitCount = 0,
    DateTime? LastAccessedAt = null,
    string? Module = null,
    IReadOnlyList<MemoryLayer>? Layers = null,
    MemoryKind? Kind = null,
    string? Area = null,
    IReadOnlyList<string>? Keywords = null,
    string? LegacyLabel = null,
    IReadOnlyList<string>? ExtraModules = null);

public enum MemoryImportMode
{
    // A memory whose id already exists is left as it is.
    Skip = 1,

    // A memory whose id already exists gets the type, project, label and description of the file.
    Overwrite = 2
}

public record MemoryImportResultDto(
    bool DryRun,
    int Total,
    int Added,
    int Updated,
    int Skipped,
    int Duplicates,
    int Invalid,
    IReadOnlyList<MemoryImportProjectDto> Projects,
    IReadOnlyList<string> Warnings);

// A project the file refers to. Registered = a clone of it is registered on this machine; otherwise the memory is
// kept under its key and shows up once that project is added.
public record MemoryImportProjectDto(string ProjectKey, string? ProjectName, int Count, bool Registered);

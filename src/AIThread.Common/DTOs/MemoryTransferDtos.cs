using AIThread.Common.Enums;

namespace AIThread.Common.DTOs;

// A portable export of short and long memory, independent of the store each level uses. Projects are named by
// ProjectKey (shared by every clone of a repository); ProjectId is only a hint for restoring on the same machine.
public record MemoryExportFile(
    string Format,
    int Version,
    DateTime ExportedAt,
    string? AppVersion,
    IReadOnlyList<MemoryExportItem> Items,
    IReadOnlyList<UnitTestExportItem>? UnitTests = null)
{
    public const string FormatName = "aithread-memory";

    // v2 adds the structured label; a v1 item (or a v2 item without Module/Kind) is imported unlabeled and relabeled
    // by MemoryRelabelWorker. v3 adds the unit test catalog of the projects (UnitTests), snapshots included.
    public const int CurrentVersion = 3;
}

// A test of a project's unit test catalog, with its source snapshot so another machine can restore it. Projects are
// named by ProjectKey like memory.
public record UnitTestExportItem(
    Guid Id,
    string ProjectKey,
    string? ProjectName,
    int Number,
    string Title,
    string? Purpose,
    string? Scenario,
    string? Module,
    string? Layers,
    string? Keywords,
    string? TargetFiles,
    string? TargetSymbols,
    string Framework,
    string? TestProject,
    string TestFile,
    string? ClassName,
    string MethodName,
    string Selector,
    string? Requires,
    UnitTestState State,
    UnitTestOrigin Origin,
    string? CreatedBy,
    Guid? CreatedThreadId,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    string? ObsoleteReason,
    Guid? ObsoleteThreadId,
    string? ObsoleteBy,
    DateTime? ObsoleteAt,
    string? QuarantineReason,
    string? RemovedBy,
    DateTime? RemovedAt,
    UnitTestResult LastResult,
    DateTime? LastRunAt,
    string? LastRunBy,
    long? LastDurationMs,
    string? LastMessage,
    string? LastSkipReason,
    string? LastInputHash,
    string? SourceSnapshot,
    string? SourceHash,
    string? SourceCommit,
    string? SourceBranch,
    string? MethodSnapshot,
    string? SupportFiles);

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

// UnitTests*: the unit test catalog of the file; Renumbered = tests that got the next free number because theirs was
// taken by another test here.
public record MemoryImportResultDto(
    bool DryRun,
    int Total,
    int Added,
    int Updated,
    int Skipped,
    int Duplicates,
    int Invalid,
    IReadOnlyList<MemoryImportProjectDto> Projects,
    IReadOnlyList<string> Warnings,
    int UnitTestsAdded = 0,
    int UnitTestsUpdated = 0,
    int UnitTestsRenumbered = 0,
    int UnitTestsSkipped = 0);

// A project the file refers to. Registered = a clone of it is registered on this machine; otherwise the memory is
// kept under its key and shows up once that project is added.
public record MemoryImportProjectDto(string ProjectKey, string? ProjectName, int Count, bool Registered);

using AIThread.Common.Enums;

namespace AIThread.Common.DTOs;

// Stacks are detected from the manifests in the root folder (".NET", "Java (Maven)", "Node.js", ...); they are not stored.
// Key identifies the project across machines (memory is shared by it); ManualKey is the one typed in the Project menu.
public record ProjectDto(
    Guid Id,
    string Name,
    string RootPath,
    IReadOnlyList<string> Stacks,
    string? Summary,
    DateTime CreatedAt,
    DateTime LastSeenAt,
    int ThreadCount,
    int SessionCount,
    int MemoryCount,
    bool Exists,
    string Key,
    ProjectKeySource KeySource,
    string? ManualKey);

public record ProjectSessionDto(string SessionId, int ThreadCount, DateTime FirstSeenAt, DateTime LastSeenAt);

// Modules are the manifest files found below the root (src/Web/Web.csproj, services/api/pom.xml, ...).
public record ProjectDetailDto(
    ProjectDto Project,
    IReadOnlyList<string> Modules,
    IReadOnlyList<ProjectSessionDto> Sessions);

public record RegisterProjectRequest(string Path);

// Empty Key clears the manual key (the git remote or the local fallback is used again).
public record SetProjectKeyRequest(string? Key);

public record RegisterProjectsRequest(IReadOnlyList<string> Paths);

public record RegisterProjectsResultDto(int Registered, int AlreadyRegistered, IReadOnlyList<string> Errors);

// A folder in the project browser. IsProject is only computed when the listing is small enough.
public record FolderEntryDto(string Name, string Path, bool IsProject, IReadOnlyList<string> Stacks);

// A project root found by browsing or scanning; Path is the root folder.
public record ProjectCandidateDto(string Name, string Path, IReadOnlyList<string> Stacks, bool IsRegistered, Guid? ProjectId);

// Path is null for the top level, where Folders lists the drives.
public record FolderBrowseDto(
    string? Path,
    string? Parent,
    IReadOnlyList<FolderEntryDto> Folders,
    IReadOnlyList<ProjectCandidateDto> Projects,
    IReadOnlyList<FolderEntryDto> QuickLocations);

public record ProjectScanDto(
    string Root,
    int Depth,
    IReadOnlyList<ProjectCandidateDto> Projects,
    int FoldersScanned,
    bool Truncated,
    long ElapsedMs);

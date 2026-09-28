namespace AiThread.Common.DTOs;

public record ProjectDto(
    Guid Id,
    string Name,
    string SolutionPath,
    string RootPath,
    string? Summary,
    DateTime CreatedAt,
    DateTime LastSeenAt,
    int ThreadCount,
    int SessionCount,
    int MemoryCount,
    bool Exists);

public record ProjectSessionDto(string SessionId, int ThreadCount, DateTime FirstSeenAt, DateTime LastSeenAt);

public record ProjectDetailDto(
    ProjectDto Project,
    IReadOnlyList<string> SolutionProjects,
    IReadOnlyList<ProjectSessionDto> Sessions);

public record RegisterProjectRequest(string Path);

public record RegisterProjectsRequest(IReadOnlyList<string> Paths);

public record RegisterProjectsResultDto(int Registered, int AlreadyRegistered, IReadOnlyList<string> Errors);

// A folder in the solution browser. HasSolution is only computed when the listing is small enough.
public record FolderEntryDto(string Name, string Path, bool HasSolution);

public record SolutionFileDto(string Name, string Path, string Directory, bool IsRegistered, Guid? ProjectId);

// Path is null for the top level, where Folders lists the drives.
public record FolderBrowseDto(
    string? Path,
    string? Parent,
    IReadOnlyList<FolderEntryDto> Folders,
    IReadOnlyList<SolutionFileDto> Solutions,
    IReadOnlyList<FolderEntryDto> QuickLocations);

public record SolutionScanDto(
    string Root,
    int Depth,
    IReadOnlyList<SolutionFileDto> Solutions,
    int FoldersScanned,
    bool Truncated,
    long ElapsedMs);

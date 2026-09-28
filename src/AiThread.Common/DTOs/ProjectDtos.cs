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

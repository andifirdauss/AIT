namespace AiThread.Service.Models;

// Where a thread operates: always a registered project identified by its .sln/.slnx file.
public sealed record ProjectContext(
    Guid ProjectId,
    string Name,
    string RootPath,
    string SolutionPath,
    IReadOnlyList<string> SolutionProjects);

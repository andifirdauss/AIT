namespace AiThread.Service.Models;

// Where a thread operates. SolutionPath is null when the working directory has no .sln/.slnx:
// the thread still runs, but nothing is registered and project memory is not used.
public sealed record ProjectContext(
    Guid? ProjectId,
    string Name,
    string RootPath,
    string? SolutionPath,
    IReadOnlyList<string> SolutionProjects)
{
    public bool IsRegistered => ProjectId is not null;
}

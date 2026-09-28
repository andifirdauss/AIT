using AiThread.Common.DTOs;
using AiThread.Service.Models;

namespace AiThread.Service.Interfaces;

// Projects are identified by their .sln/.slnx file so concurrent sessions never mix contexts.
public interface IProjectService
{
    // Decides once per Claude Code session whether it belongs to a .sln/.slnx project (registering it on the
    // first prompt). Null means the session is outside any solution and AiThread must not orchestrate it.
    Task<ProjectContext?> ResolveSessionAsync(string? sessionId, string? workingDirectory, string? solutionHint, CancellationToken cancellationToken = default);

    Task<ProjectContext?> GetContextAsync(Guid projectId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProjectDto>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<ProjectDetailDto?> GetDetailAsync(Guid id, CancellationToken cancellationToken = default);

    Task<ProjectDto> RegisterAsync(string path, CancellationToken cancellationToken = default);

    Task<RegisterProjectsResultDto> RegisterManyAsync(IReadOnlyList<string> solutionPaths, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    // Folder browser for picking a solution file; an empty path lists the drives.
    Task<FolderBrowseDto> BrowseAsync(string? path, CancellationToken cancellationToken = default);

    // Recursive search for .sln/.slnx files below a folder.
    Task<SolutionScanDto> ScanAsync(string path, int depth, CancellationToken cancellationToken = default);
}

using AIThread.Common.DTOs;
using AIThread.Service.Models;

namespace AIThread.Service.Interfaces;

// Projects are identified by their root folder (the one holding .git or a build manifest) so concurrent sessions
// never mix contexts.
public interface IProjectService
{
    // Decides once per agent session whether it belongs to a project (registering it on the first prompt). Null means
    // no project marker was found around the session and AIThread must not orchestrate it (and keeps no memory for it).
    Task<ProjectContext?> ResolveSessionAsync(string? sessionId, string? workingDirectory, string? projectHint, CancellationToken cancellationToken = default);

    Task<ProjectContext?> GetContextAsync(Guid projectId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProjectDto>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<ProjectDetailDto?> GetDetailAsync(Guid id, CancellationToken cancellationToken = default);

    // Registers the project a folder belongs to; a folder without any marker is registered as it is.
    Task<ProjectDto> RegisterAsync(string path, CancellationToken cancellationToken = default);

    Task<RegisterProjectsResultDto> RegisterManyAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken = default);

    // Memory in a shared (external) store belongs to every developer of the project and is kept unless
    // includeSharedMemory; memory on this machine only is always removed.
    Task<bool> DeleteAsync(Guid id, bool includeSharedMemory = false, CancellationToken cancellationToken = default);

    // Sets the key typed in the Project menu (empty clears it); .aithread/project.json still wins over it.
    Task<ProjectDto?> SetKeyAsync(Guid id, string? key, CancellationToken cancellationToken = default);

    // Recomputes the key of every registered project and fills the key of memory written before keys existed.
    Task RefreshKeysAsync(CancellationToken cancellationToken = default);

    // Folder browser for picking a project folder; an empty path lists the drives.
    Task<FolderBrowseDto> BrowseAsync(string? path, CancellationToken cancellationToken = default);

    // Recursive search for project folders below a folder.
    Task<ProjectScanDto> ScanAsync(string path, int depth, CancellationToken cancellationToken = default);
}

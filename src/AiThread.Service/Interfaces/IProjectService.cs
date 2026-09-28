using AiThread.Common.DTOs;
using AiThread.Service.Models;

namespace AiThread.Service.Interfaces;

// Projects are identified by their .sln/.slnx file so concurrent sessions never mix contexts.
public interface IProjectService
{
    // Locates the solution from a working directory (or an explicit hint) and registers/updates the project.
    Task<ProjectContext?> ResolveAsync(string? workingDirectory, string? solutionHint, CancellationToken cancellationToken = default);

    Task<ProjectContext?> GetContextAsync(Guid projectId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProjectDto>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<ProjectDetailDto?> GetDetailAsync(Guid id, CancellationToken cancellationToken = default);

    Task<ProjectDto> RegisterAsync(string path, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}

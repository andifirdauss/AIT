using AIThread.Common.DTOs;

namespace AIThread.Service.Interfaces;

public interface IGitEnvironmentTracker
{
    Task<IReadOnlyList<ProjectEnvironmentDto>?> ListAsync(Guid projectId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProjectEnvironmentDto>?> SaveAsync(Guid projectId, IReadOnlyList<SaveProjectEnvironment> environments,
        CancellationToken cancellationToken = default);

    Task<int> RefreshAsync(Guid projectId, CancellationToken cancellationToken = default);

    Task<IReadOnlySet<Guid>> ThreadsInAsync(string projectKey, string environment, CancellationToken cancellationToken = default);
}

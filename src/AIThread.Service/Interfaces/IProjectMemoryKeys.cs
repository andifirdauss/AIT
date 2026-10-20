using AIThread.Common.Entities;

namespace AIThread.Service.Interfaces;

public interface IProjectMemoryKeys
{
    bool ShortStoreIsShared { get; }

    bool LongStoreIsShared { get; }

    Task<int> BackfillAsync(IReadOnlyList<Project> projects, CancellationToken cancellationToken);

    Task<int> RekeyAsync(string oldKey, string newKey, string projectName, CancellationToken cancellationToken);

    Task<int> DeleteAsync(Guid projectId, string projectKey, bool includeShared, CancellationToken cancellationToken);

    Task HandOverAsync(Guid fromProjectId, Guid toProjectId, CancellationToken cancellationToken);
}

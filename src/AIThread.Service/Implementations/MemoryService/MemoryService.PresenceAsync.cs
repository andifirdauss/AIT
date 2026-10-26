using AIThread.Service.Git;

namespace AIThread.Service.Implementations;

public sealed partial class MemoryService
{
    private async Task<IReadOnlyDictionary<Guid, MemoryGitPresence>> PresenceAsync(ProjectScope? scope, IEnumerable<Guid?> threadIds,
        CancellationToken cancellationToken)
    {
        if (scope is null) return new Dictionary<Guid, MemoryGitPresence>();
        var ids = threadIds.OfType<Guid>().Distinct().ToList();
        return ids.Count == 0 ? new Dictionary<Guid, MemoryGitPresence>() : await gitChanges.PresenceAsync(scope.Id, ids, cancellationToken);
    }
}

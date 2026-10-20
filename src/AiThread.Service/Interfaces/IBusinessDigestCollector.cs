using AiThread.Service.Business;

namespace AiThread.Service.Interfaces;

public interface IBusinessDigestCollector
{
    Task<int> CountPendingAsync(string projectKey, CancellationToken cancellationToken);

    Task<IReadOnlyList<(string Key, string Name)>> PendingProjectsAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<ThreadDigest>> NextBatchAsync(string projectKey, CancellationToken cancellationToken);

    Task MarkDigestedAsync(IReadOnlyCollection<Guid> threadIds, CancellationToken cancellationToken);

    Task<int> PruneAttachmentsAsync(TimeSpan maxAge, CancellationToken cancellationToken);
}

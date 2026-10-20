using AIThread.Service.Business;

namespace AIThread.Service.Interfaces;

public interface IBusinessMemoryWriter
{
    Task<string> ResolveModelAsync(CancellationToken cancellationToken);

    Task<BusinessBatchResult> WriteAsync(string projectKey, string projectName, IReadOnlyList<ThreadDigest> threads, CancellationToken cancellationToken);
}

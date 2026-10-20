namespace AiThread.Service.Implementations;

public sealed partial class MemoryCatalogService
{
    public async Task RejectMergeAsync(Guid id, CancellationToken cancellationToken = default) =>
        await merger.RejectAsync(id, await DeciderAsync(cancellationToken), cancellationToken);
}

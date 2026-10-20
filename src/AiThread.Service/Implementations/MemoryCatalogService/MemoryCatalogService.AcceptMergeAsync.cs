namespace AiThread.Service.Implementations;

public sealed partial class MemoryCatalogService
{
    public async Task AcceptMergeAsync(Guid id, CancellationToken cancellationToken = default) =>
        await merger.AcceptAsync(id, await DeciderAsync(cancellationToken), cancellationToken);
}

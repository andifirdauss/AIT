namespace AiThread.Service.Implementations;

public sealed partial class MemoryCatalogService
{
    public async Task UndoMergeAsync(Guid id, CancellationToken cancellationToken = default) =>
        await merger.UndoAsync(id, await DeciderAsync(cancellationToken), cancellationToken);
}

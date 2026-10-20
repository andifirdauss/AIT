namespace AiThread.Service.Implementations;

public sealed partial class MemoryCatalogService
{
    public async Task<int> CheckModulesAsync(Guid? projectId, bool knowledge, CancellationToken cancellationToken = default) =>
        await merger.CheckAsync(await KeyAsync(projectId, knowledge, cancellationToken), null, cancellationToken);
}

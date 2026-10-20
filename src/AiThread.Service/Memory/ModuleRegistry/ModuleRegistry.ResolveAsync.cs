namespace AiThread.Service.Memory;

public sealed partial class ModuleRegistry
{
    public async Task<string?> ResolveAsync(string key, string? name, CancellationToken cancellationToken = default) =>
        Resolve(await ListAsync(key, includeMerged: true, cancellationToken), name);
}

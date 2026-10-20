using AIThread.Common.DTOs;
using AIThread.Common.Entities;
using AIThread.Common.Memory;
using AIThread.Service.Memory;

namespace AIThread.Service.Implementations;

public sealed partial class MemoryService
{
    // Explicit modules (resolved against the registry), else the modules the text names.
    private async Task<IReadOnlyCollection<string>?> WantedModulesAsync(ProjectScope? scope, MemorySearch search, IReadOnlyList<string> tokens, CancellationToken cancellationToken)
    {
        var registryModules = new List<ProjectModule>();
        try
        {
            if (scope?.Key is { } key) registryModules.AddRange(await registry.ListAsync(key, includeMerged: true, cancellationToken));
            registryModules.AddRange(await registry.ListAsync(MemoryLabels.KnowledgeKey, includeMerged: true, cancellationToken));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LongStoreFailed(ex, "Could not read the module list");
        }

        var wanted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in (search.Modules ?? []).Append(search.Module).Where(n => !string.IsNullOrWhiteSpace(n)))
            wanted.Add(ModuleRegistry.Resolve(registryModules, name) ?? MemoryLabels.Module(name)!);
        if (wanted.Count == 0) wanted.UnionWith(MemoryRanker.DetectModules(registryModules, tokens));
        return wanted.Count == 0 ? null : wanted;
    }
}

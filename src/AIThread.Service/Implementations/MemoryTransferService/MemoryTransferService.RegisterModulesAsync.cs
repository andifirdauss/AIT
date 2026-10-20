using AIThread.Common.Enums;
using AIThread.Common.Memory;

namespace AIThread.Service.Implementations;

public sealed partial class MemoryTransferService
{
    // The modules of imported labels join the module list of their key, under the name the registry resolves to.
    private async Task RegisterModulesAsync(IEnumerable<Prepared> items, CancellationToken cancellationToken)
    {
        foreach (var group in items.Where(i => i.Labels is not null).GroupBy(i => i.Type == MemoryType.Knowledge ? MemoryLabels.KnowledgeKey : i.Key))
        {
            if (group.Key is not { } key) continue;
            foreach (var item in group)
            {
                var (module, _) = await registry.EnsureAsync(key, item.Labels!.Module, cancellationToken: cancellationToken);
                var resolved = new List<string>();
                foreach (var name in item.Labels.ExtraModules ?? [])
                    resolved.Add((await registry.EnsureAsync(key, name, cancellationToken: cancellationToken)).Name);
                var extra = MemoryLabels.ExtraModules(resolved, module);
                if (module == item.Labels.Module && extra.SequenceEqual(item.Labels.ExtraModules ?? [])) continue;
                item.Labels = item.Labels with { Module = module, ExtraModules = extra };
                item.Label = item.Labels.Composed;
            }
        }
    }
}

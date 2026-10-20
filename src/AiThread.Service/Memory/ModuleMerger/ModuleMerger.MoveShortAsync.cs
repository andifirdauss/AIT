using AiThread.Common.Enums;
using AiThread.Common.Memory;
using Microsoft.Extensions.Logging;

namespace AiThread.Service.Memory;

public sealed partial class ModuleMerger
{
    private async Task MoveShortAsync(string key, bool knowledge, string from, string into, HashSet<Guid>? restrictTo, List<Guid> moved)
    {
        if (!shortStore.IsConnected) return;
        try
        {
            var records = (await shortStore.GetAllAsync())
                .Where(r => knowledge ? r.Type == MemoryType.Knowledge : r.ProjectKey == key && r.Type != MemoryType.Knowledge)
                .Where(r => restrictTo is null || restrictTo.Contains(r.Id))
                .ToList();
            foreach (var record in records)
            {
                if (Rename(record.Module, record.ExtraModules, from, into) is not { } renamed) continue;
                var (module, extra) = renamed;
                var label = record.Kind is { } kind
                    ? MemoryLabels.Compose(MemoryLabels.Display(record.Area, module!, MemoryLabels.ParseLayers(record.Layers), kind, MemoryLabels.SplitCsv(extra)),
                        MemoryLabels.SplitCsv(record.Keywords))
                    : record.Label;
                await shortStore.SaveAsync(record with { Module = module, ExtraModules = extra, Label = label });
                moved.Add(record.Id);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not move short memories from module {From} to {Into}.", from, into);
        }
    }
}

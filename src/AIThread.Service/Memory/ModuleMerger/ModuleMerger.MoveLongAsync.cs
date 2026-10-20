using AIThread.Common.Enums;
using AIThread.Common.Memory;
using AIThread.Data.Memory;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Memory;

public sealed partial class ModuleMerger
{
    // restrictTo: only these rows (an undo); moved collects the ids that changed.
    private async Task MoveLongAsync(string key, bool knowledge, string from, string into, HashSet<Guid>? restrictTo, List<Guid> moved, CancellationToken cancellationToken)
    {
        try
        {
            await using var db = await longStore.CreateAsync(cancellationToken);
            var query = knowledge
                ? db.LongMemories.Where(m => m.Type == MemoryType.Knowledge)
                : db.LongMemories.Where(m => m.ProjectKey == key && m.Type != MemoryType.Knowledge);
            var rows = await query.Where(m => m.Module == from || (m.ExtraModules != null && ("," + m.ExtraModules + ",").Contains("," + from + ",")))
                .ToListAsync(cancellationToken);
            foreach (var row in rows.Where(r => restrictTo is null || restrictTo.Contains(r.Id)))
            {
                if (Rename(row.Module, row.ExtraModules, from, into) is not { } renamed) continue;
                var (module, extra) = renamed;
                row.Module = module;
                row.ExtraModules = extra;
                if (row.Kind is { } kind)
                    row.Label = MemoryLabels.Compose(MemoryLabels.Display(row.Area, module!, MemoryLabels.ParseLayers(row.Layers), kind, MemoryLabels.SplitCsv(extra)),
                        MemoryLabels.SplitCsv(row.Keywords));
                row.UpdatedAt = DateTime.UtcNow;
                moved.Add(row.Id);
            }
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogStoreWarning(ex, $"Could not move long memories from module {from} to {into}");
        }
    }
}

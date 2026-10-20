using AIThread.Common.Enums;
using AIThread.Common.Memory;
using AIThread.Data.Memory;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Memory;

public sealed partial class ModuleMerger
{
    // Memories plus mapped files per module.
    private async Task<Dictionary<string, int>> UsageAsync(string key, CancellationToken cancellationToken)
    {
        var usage = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        try
        {
            await using (var catalog = await longStore.CreateCatalogAsync(cancellationToken))
            {
                var files = await catalog.CodeMapEntries.AsNoTracking().Where(e => e.ProjectKey == key)
                    .GroupBy(e => e.Module).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(cancellationToken);
                foreach (var f in files) usage[f.Key] = f.Count;
            }
            await using var db = await longStore.CreateAsync(cancellationToken);
            var knowledge = key == MemoryLabels.KnowledgeKey;
            var memories = await (knowledge ? db.LongMemories.Where(m => m.Type == MemoryType.Knowledge) : db.LongMemories.Where(m => m.ProjectKey == key))
                .Where(m => m.Module != null).GroupBy(m => m.Module!).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(cancellationToken);
            foreach (var m in memories) usage[m.Key] = usage.GetValueOrDefault(m.Key) + m.Count;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogStoreWarning(ex, "Could not count module usage");
        }
        return usage;
    }
}

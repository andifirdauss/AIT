using AiThread.Common.DTOs;
using AiThread.Common.Enums;
using AiThread.Common.Memory;
using Microsoft.EntityFrameworkCore;

namespace AiThread.Service.Implementations;

public sealed partial class MemoryService
{
    // Long memory may live in an external database too; an outage there only hides those memories, the thread still runs.
    private async Task<IReadOnlyList<MemoryIndexEntry>> LongIndexAsync(ProjectScope? scope, CancellationToken cancellationToken)
    {
        try
        {
            await using var db = await longStore.CreateAsync(cancellationToken);
            var rows = await WhereVisible(db.LongMemories.AsNoTracking(), scope, includeKnowledge: true)
                .Select(m => new
                {
                    m.Id, m.Type, m.Label, m.CreatedAt, m.Module, m.ExtraModules, m.Layers, m.Kind, m.Area, m.Keywords, m.HitCount, m.LabelVersion
                })
                .ToListAsync(cancellationToken);
            return rows.Select(m => m.LabelVersion is null || m.Module is null || m.Kind is null
                ? new MemoryIndexEntry(m.Id, MemoryLevel.Long, m.Type, m.Label, m.CreatedAt, HitCount: m.HitCount)
                : new MemoryIndexEntry(m.Id, MemoryLevel.Long, m.Type, m.Label, m.CreatedAt, m.Module, MemoryLabels.ParseLayers(m.Layers), m.Kind,
                    m.Area, MemoryLabels.SplitCsv(m.Keywords), m.HitCount, MemoryLabels.SplitCsv(m.ExtraModules))).ToList();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LongStoreFailed(ex, "Could not read the long memory index");
            return [];
        }
    }
}

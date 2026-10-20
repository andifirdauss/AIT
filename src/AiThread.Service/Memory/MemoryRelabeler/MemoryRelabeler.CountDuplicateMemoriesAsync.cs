using AiThread.Common.DTOs;
using AiThread.Common.Enums;
using AiThread.Common.Memory;
using Microsoft.EntityFrameworkCore;

namespace AiThread.Service.Memory;

public sealed partial class MemoryRelabeler
{
    private async Task<int> CountDuplicateMemoriesAsync(string key, CancellationToken cancellationToken)
    {
        await using var db = await longStore.CreateAsync(cancellationToken);
        var knowledge = key == MemoryLabels.KnowledgeKey;
        var rows = await (knowledge ? db.LongMemories.Where(m => m.Type == MemoryType.Knowledge) : db.LongMemories.Where(m => m.ProjectKey == key))
            .Where(m => m.LabelVersion != null && m.Module != null && m.Kind != null)
            .Select(m => new { m.Id, m.Type, m.Label, m.CreatedAt, m.Module, m.Kind, m.Keywords })
            .ToListAsync(cancellationToken);
        var entries = rows.Select(r => new MemoryIndexEntry(r.Id, MemoryLevel.Long, r.Type, r.Label, r.CreatedAt, r.Module, null, r.Kind, null,
            MemoryLabels.SplitCsv(r.Keywords))).ToList();
        return MemoryRanker.FindDuplicates(entries).Count;
    }
}

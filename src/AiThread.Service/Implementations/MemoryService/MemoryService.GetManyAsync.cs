using AiThread.Common.DTOs;
using AiThread.Common.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AiThread.Service.Implementations;

public sealed partial class MemoryService
{
    public async Task<IReadOnlyList<MemoryItemDto>> GetManyAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default)
    {
        var idList = ids.Distinct().Take(50).ToList();
        if (idList.Count == 0) return [];

        var results = new List<MemoryItemDto>();
        var projects = await LocalProjectsAsync(cancellationToken);
        if (shortStore.IsConnected)
        {
            foreach (var id in idList)
            {
                try
                {
                    if (await shortStore.GetAsync(id) is { } record) results.Add(ToDto(record, projects));
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Could not read short memory {Id}", id);
                }
            }
        }

        var remaining = idList.Except(results.Select(r => r.Id)).ToList();
        if (remaining.Count == 0) return results;

        var longs = new List<LongMemory>();
        try
        {
            await using var db = await longStore.CreateAsync(cancellationToken);
            longs = await db.LongMemories.AsNoTracking()
                .Where(m => remaining.Contains(m.Id))
                .ToListAsync(cancellationToken);

            // Usage statistics help to spot memories worth keeping.
            await db.LongMemories.Where(m => remaining.Contains(m.Id)).ExecuteUpdateAsync(s => s
                .SetProperty(m => m.HitCount, m => m.HitCount + 1)
                .SetProperty(m => m.LastAccessedAt, DateTime.UtcNow), cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LongStoreFailed(ex, "Could not read long memories");
        }
        results.AddRange(longs.Select(m => ToDto(m, projects)));

        remaining = remaining.Except(longs.Select(m => m.Id)).ToList();
        if (remaining.Count > 0) results.AddRange(await BusinessPagesAsync(remaining, projects, cancellationToken));
        return results;
    }
}

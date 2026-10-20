using AiThread.Common.Entities;
using AiThread.Common.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AiThread.Service.Memory;

public sealed partial class CodeMapper
{
    private async Task SaveAsync(string key, IReadOnlyList<CodeMapHit> hits, CancellationToken cancellationToken, bool overwriteManual = false)
    {
        if (hits.Count == 0) return;
        await using var db = await store.CreateCatalogAsync(cancellationToken);
        foreach (var chunk in hits.DistinctBy(h => h.Path, StringComparer.OrdinalIgnoreCase).Chunk(200))
        {
            var paths = chunk.Select(h => h.Path).ToList();
            var existing = await db.CodeMapEntries.Where(e => e.ProjectKey == key && paths.Contains(e.Path))
                .ToDictionaryAsync(e => e.Path, StringComparer.OrdinalIgnoreCase, cancellationToken);
            var now = DateTime.UtcNow;
            foreach (var hit in chunk)
            {
                if (existing.TryGetValue(hit.Path, out var row))
                {
                    if (row.Source == CodeMapSource.Manual && !overwriteManual) continue;
                    row.Module = hit.Module;
                    row.Layer = hit.Layer;
                    row.Area = hit.Area;
                    row.Source = hit.Source;
                    row.UpdatedAt = now;
                }
                else
                {
                    db.CodeMapEntries.Add(new CodeMapEntry
                    {
                        Id = Guid.NewGuid(), ProjectKey = key, Path = hit.Path, Module = hit.Module, Layer = hit.Layer,
                        Area = hit.Area, Source = hit.Source, CreatedAt = now
                    });
                }
            }
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException ex)
            {
                // Another developer's AiThread mapped the same file meanwhile; their entry stands.
                logger.LogDebug(ex, "Code map entries of {Key} were written concurrently.", key);
            }
        }
    }
}

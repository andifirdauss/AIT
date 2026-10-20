using AIThread.Common.Entities;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Memory;

public sealed partial class CodeMapper
{
    // Stored entries of the given paths, for callers that only read the map.
    public async Task<IReadOnlyDictionary<string, CodeMapEntry>> StoredAsync(string key, IReadOnlyCollection<string> paths, CancellationToken cancellationToken)
    {
        if (paths.Count == 0 || string.IsNullOrEmpty(key)) return new Dictionary<string, CodeMapEntry>();
        await using var db = await store.CreateCatalogAsync(cancellationToken);
        var result = new Dictionary<string, CodeMapEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var chunk in paths.Distinct(StringComparer.OrdinalIgnoreCase).Chunk(200))
        {
            var rows = await db.CodeMapEntries.AsNoTracking().Where(e => e.ProjectKey == key && chunk.Contains(e.Path)).ToListAsync(cancellationToken);
            foreach (var row in rows) result[row.Path] = row;
        }
        return result;
    }
}

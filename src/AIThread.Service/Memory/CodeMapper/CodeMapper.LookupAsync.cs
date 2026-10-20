using AIThread.Common.Entities;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Memory;

public sealed partial class CodeMapper
{
    // Stored entries for the files and identifiers a text names, without touching the disk (the project may not be
    // cloned on this machine): used to label memory from its description.
    public async Task<IReadOnlyList<CodeMapEntry>> LookupAsync(string key, string? text, int max, CancellationToken cancellationToken)
    {
        var (paths, identifiers) = CodeMapHeuristics.References(text, 10);
        if ((paths.Count == 0 && identifiers.Count == 0) || string.IsNullOrEmpty(key)) return [];

        var result = new List<CodeMapEntry>();
        await using var db = await store.CreateCatalogAsync(cancellationToken);
        foreach (var path in paths.Select(p => p.Replace('\\', '/').TrimStart('.', '/')))
        {
            var suffix = "/" + path;
            result.AddRange(await db.CodeMapEntries.AsNoTracking()
                .Where(e => e.ProjectKey == key && (e.Path == path || e.Path.EndsWith(suffix)))
                .Take(2).ToListAsync(cancellationToken));
        }
        foreach (var identifier in identifiers)
        {
            var marker = "/" + identifier + ".";
            result.AddRange(await db.CodeMapEntries.AsNoTracking()
                .Where(e => e.ProjectKey == key && (e.Path.Contains(marker) || e.Path.StartsWith(identifier + ".")))
                .Take(2).ToListAsync(cancellationToken));
            if (result.Count >= max) break;
        }
        return result.DistinctBy(e => e.Id).Take(max).ToList();
    }
}

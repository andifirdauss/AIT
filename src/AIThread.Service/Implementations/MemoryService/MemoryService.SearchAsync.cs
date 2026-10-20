using AIThread.Common.DTOs;
using AIThread.Common.Enums;
using AIThread.Common.Memory;
using AIThread.Service.Memory;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Implementations;

public sealed partial class MemoryService
{
    public async Task<IReadOnlyList<MemoryIndexEntry>> SearchAsync(MemorySearch search, CancellationToken cancellationToken = default)
    {
        var take = Math.Max(1, search.Take);
        var scope = (await LocalProjectsAsync(cancellationToken)).Find(search.ProjectId);

        var entries = (await SafeShortAsync())
            .Where(r => r.Type == MemoryType.Knowledge || (scope is not null && scope.Owns(r.ProjectKey, r.ProjectId)))
            .Select(ToIndexEntry)
            .ToList();
        entries.AddRange(await LongIndexAsync(scope, cancellationToken));

        var tokens = MemoryLabels.Tokens(string.Join(' ', (search.Keywords ?? []).Append(search.Text ?? string.Empty)));
        var modules = await WantedModulesAsync(scope, search, tokens, cancellationToken);

        // Business pages and unlabeled memory get their own share, so neither crowds the labeled memory out.
        var pages = MemoryRanker.RankPages(await BusinessIndexAsync(scope, cancellationToken), modules, tokens, Math.Min(8, Math.Max(1, take / 3)));
        var unlabeledShare = entries.Any(e => !e.Labeled) ? Math.Max(3, take / 4) : 0;
        var labeled = MemoryRanker.RankLabeled(entries, modules, search.Layer, search.Kind, tokens, take - pages.Count - Math.Min(unlabeledShare, 3));
        var unlabeled = MemoryRanker.RankUnlabeled(entries, tokens, Math.Min(unlabeledShare, take - pages.Count - labeled.Count));
        return pages.Concat(labeled).Concat(unlabeled).Take(take).ToList();
    }
}

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
        // Git is asked only about the best candidates (twice what is shown): memory whose change was thrown away drops out,
        // memory whose change is not in this branch comes after the rest (OrderBy keeps the ranking otherwise).
        var labeledTake = take - pages.Count - Math.Min(unlabeledShare, 3);
        var labeled = (await WithGitAsync(scope, MemoryRanker.RankLabeled(entries, modules, search.Layer, search.Kind, tokens, labeledTake * 2), cancellationToken))
            .OrderBy(e => e.GitNote is null ? 0 : 1)
            .Take(Math.Max(0, labeledTake))
            .ToList();
        var unlabeledTake = Math.Min(unlabeledShare, take - pages.Count - labeled.Count);
        var unlabeled = (await WithGitAsync(scope, MemoryRanker.RankUnlabeled(entries, tokens, unlabeledTake * 2), cancellationToken))
            .OrderBy(e => e.GitNote is null ? 0 : 1)
            .Take(Math.Max(0, unlabeledTake))
            .ToList();
        return pages.Concat(labeled).Concat(unlabeled).Take(take).ToList();
    }
}

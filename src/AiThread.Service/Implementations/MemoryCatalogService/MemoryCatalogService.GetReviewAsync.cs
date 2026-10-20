using AiThread.Common.DTOs;
using AiThread.Common.Enums;
using AiThread.Common.Memory;
using AiThread.Service.Memory;

namespace AiThread.Service.Implementations;

public sealed partial class MemoryCatalogService
{
    public async Task<MemoryReviewDto> GetReviewAsync(Guid? projectId, CancellationToken cancellationToken = default)
    {
        var memories = (await memoryService.QueryAsync(new MemoryQuery(null, null, projectId, null, 1, 200, NeedsReview: true), cancellationToken)).Items;

        var merges = new List<ModuleMergeDto>();
        if (projectId is not null) merges.AddRange(await merger.ListAsync(await KeyAsync(projectId, false, cancellationToken), ModuleMergeStatus.Suggested, 50, cancellationToken));
        merges.AddRange(await merger.ListAsync(MemoryLabels.KnowledgeKey, ModuleMergeStatus.Suggested, 50, cancellationToken));

        var labeled = new List<MemoryItemDto>();
        for (var page = 1; page <= 10; page++)
        {
            var result = await memoryService.QueryAsync(new MemoryQuery(null, null, projectId, null, page, 200, Labeled: true), cancellationToken);
            labeled.AddRange(result.Items);
            if (labeled.Count >= result.Total) break;
        }
        // Pairs only within one project (or within knowledge memory): two projects may both have a module "User".
        var byId = labeled.ToDictionary(m => m.Id);
        var duplicates = labeled
            .GroupBy(m => m.Type == MemoryType.Knowledge ? MemoryLabels.KnowledgeKey : m.ProjectKey ?? m.ProjectId?.ToString() ?? string.Empty)
            .SelectMany(g => MemoryRanker.FindDuplicates(g.Select(m =>
                new MemoryIndexEntry(m.Id, m.Level, m.Type, m.Label, m.CreatedAt, m.Module, m.Layers, m.Kind, m.Area, m.Keywords, ExtraModules: m.ExtraModules)).ToList()))
            .OrderByDescending(d => d.Similarity)
            .Take(50)
            .Select(d => new MemoryDuplicateDto(byId[d.First.Id], byId[d.Second.Id], Math.Round(d.Similarity, 2)))
            .ToList();

        return new MemoryReviewDto(memories, merges, duplicates);
    }
}

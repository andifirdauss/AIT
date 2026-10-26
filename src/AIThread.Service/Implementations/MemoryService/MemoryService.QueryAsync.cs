using AIThread.Common.DTOs;
using AIThread.Common.Enums;
using AIThread.Common.Memory;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Implementations;

public sealed partial class MemoryService
{
    public async Task<PagedResult<MemoryItemDto>> QueryAsync(MemoryQuery query, CancellationToken cancellationToken = default)
    {
        var page = Math.Max(1, query.Page);
        var pageSize = query.PageSize <= 0 ? 50 : Math.Clamp(query.PageSize, 1, 200);
        var search = query.Search?.Trim();
        var module = string.IsNullOrWhiteSpace(query.Module) ? null : query.Module.Trim();
        var items = new List<MemoryItemDto>();
        var projects = await LocalProjectsAsync(cancellationToken);

        // A project filter matches every memory of the project key, whichever developer wrote it. An unknown project
        // id still filters (to its own legacy rows) rather than showing everything.
        var scope = query.ProjectId is { } filterId ? projects.Find(filterId) ?? ProjectScope.Unknown(filterId) : null;

        if (query.Level is null or MemoryLevel.Short)
        {
            var shorts = (await SafeShortAsync()).Where(r =>
                (query.Type is null || r.Type == query.Type)
                && (scope is null || scope.Owns(r.ProjectKey, r.ProjectId))
                && (module is null || MemoryLabels.HasModule(r.Module, r.ExtraModules, module))
                && (query.Kind is null || r.Kind == query.Kind)
                && (query.Layer is null || MemoryLabels.ParseLayers(r.Layers).Contains(query.Layer.Value))
                && (query.Labeled is null || IsLabeled(r.LabelVersion, r.Module, r.Kind) == query.Labeled)
                && (query.NeedsReview is null || (r.ReviewReason is not null) == query.NeedsReview)
                && (string.IsNullOrEmpty(search)
                    || r.Label.Contains(search, StringComparison.OrdinalIgnoreCase)
                    || r.Description.Contains(search, StringComparison.OrdinalIgnoreCase)));
            items.AddRange(shorts.Select(r => ToDto(r, projects)));
        }

        if (query.Level is null or MemoryLevel.Long)
        {
            await using var db = await longStore.CreateAsync(cancellationToken);
            var longQuery = db.LongMemories.AsNoTracking().AsQueryable();
            if (query.Type is { } type) longQuery = longQuery.Where(m => m.Type == type);
            if (scope is not null) longQuery = WhereVisible(longQuery, scope, includeKnowledge: false);
            if (module is not null)
                longQuery = longQuery.Where(m => m.Module == module || (m.ExtraModules != null && ("," + m.ExtraModules + ",").Contains("," + module + ",")));
            if (query.Kind is { } kind) longQuery = longQuery.Where(m => m.Kind == kind);
            if (query.Layer is { } layer)
            {
                var layerName = layer.ToString();
                longQuery = longQuery.Where(m => m.Layers != null && ("," + m.Layers + ",").Contains("," + layerName + ","));
            }
            if (query.Labeled == true) longQuery = longQuery.Where(m => m.LabelVersion != null && m.Module != null && m.Kind != null);
            if (query.Labeled == false) longQuery = longQuery.Where(m => m.LabelVersion == null || m.Module == null || m.Kind == null);
            if (query.NeedsReview == true) longQuery = longQuery.Where(m => m.ReviewReason != null);
            if (query.NeedsReview == false) longQuery = longQuery.Where(m => m.ReviewReason == null);
            if (!string.IsNullOrEmpty(search)) longQuery = longQuery.Where(m => m.Label.Contains(search) || m.Description.Contains(search));

            var longs = await longQuery.OrderByDescending(m => m.CreatedAt).Take(page * pageSize + 500).ToListAsync(cancellationToken);
            items.AddRange(longs.Select(m => ToDto(m, projects)));
        }

        var ordered = items.OrderByDescending(i => i.CreatedAt).ToList();
        var shown = await WithGitStatesAsync(ordered.Skip((page - 1) * pageSize).Take(pageSize).ToList(), cancellationToken);
        return new PagedResult<MemoryItemDto>(shown, ordered.Count, page, pageSize);
    }
}

using AIThread.Common.DTOs;
using AIThread.Common.Entities;
using AIThread.Common.Enums;
using AIThread.Common.Memory;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Implementations;

public sealed partial class MemoryService
{
    public async Task<IReadOnlyList<MemoryItemDto>> GetModuleMemoryAsync(Guid? projectId, IReadOnlyList<string> modules, int changesPerModule,
        CancellationToken cancellationToken = default)
    {
        var projects = await LocalProjectsAsync(cancellationToken);
        if (projects.Find(projectId) is not { } scope) return [];
        var wanted = modules.Where(m => !string.IsNullOrWhiteSpace(m)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        var items = (await SafeShortAsync())
            .Where(r => r.Type == MemoryType.Project && r.Kind is MemoryKind.Change or MemoryKind.Summary && scope.Owns(r.ProjectKey, r.ProjectId))
            .Select(r => ToDto(r, projects))
            .ToList();
        try
        {
            await using var db = await longStore.CreateAsync(cancellationToken);
            var visible = WhereVisible(db.LongMemories.AsNoTracking(), scope, includeKnowledge: false);
            var longs = new List<LongMemory>();
            if (wanted.Count > 0)
                longs.AddRange(await visible.Where(m => m.Kind == MemoryKind.Summary && m.Module != null && wanted.Contains(m.Module)).ToListAsync(cancellationToken));
            if (changesPerModule > 0)
            {
                var changes = visible.Where(m => m.Kind == MemoryKind.Change);
                // Extra modules are a CSV column: their rows are matched below, after reading.
                if (wanted.Count > 0) changes = changes.Where(m => (m.Module != null && wanted.Contains(m.Module)) || m.ExtraModules != null);
                longs.AddRange(await changes.OrderByDescending(m => m.CreatedAt).Take(MaxChangeRows).ToListAsync(cancellationToken));
            }
            items.AddRange(longs.Select(m => ToDto(m, projects)));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LongStoreFailed(ex, "Could not read the module memory");
        }

        var newest = items.Where(i => i.Kind == MemoryKind.Change).OrderByDescending(i => i.CreatedAt).ToList();
        if (wanted.Count == 0) return newest.Take(changesPerModule).ToList();

        // Per module: its summary, then its newest change entries. An entry of several modules is listed once.
        var result = new List<MemoryItemDto>();
        foreach (var module in wanted)
        {
            result.AddRange(items.Where(i => i.Kind == MemoryKind.Summary && string.Equals(i.Module, module, StringComparison.OrdinalIgnoreCase)).Take(1));
            result.AddRange(newest.Where(i => MemoryLabels.HasModule(i.Module, MemoryLabels.JoinCsv(i.ExtraModules), module)).Take(changesPerModule));
        }
        return result.DistinctBy(i => i.Id).ToList();
    }
}

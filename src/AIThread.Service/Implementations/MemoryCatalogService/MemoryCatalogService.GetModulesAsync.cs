using AIThread.Common.DTOs;
using AIThread.Common.Enums;
using AIThread.Common.Memory;
using AIThread.Data.Memory;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Implementations;

public sealed partial class MemoryCatalogService
{
    public async Task<IReadOnlyList<ProjectModuleDto>> GetModulesAsync(Guid? projectId, bool knowledge, CancellationToken cancellationToken = default)
    {
        var key = await KeyAsync(projectId, knowledge, cancellationToken);
        var modules = await registry.ListAsync(key, cancellationToken: cancellationToken);

        Dictionary<string, int> files;
        await using (var catalog = await longStore.CreateCatalogAsync(cancellationToken))
            files = await catalog.CodeMapEntries.AsNoTracking().Where(e => e.ProjectKey == key)
                .GroupBy(e => e.Module).Select(g => new { g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Key, x => x.Count, StringComparer.OrdinalIgnoreCase, cancellationToken);

        var memories = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        try
        {
            await using var db = await longStore.CreateAsync(cancellationToken);
            var rows = await (knowledge ? db.LongMemories.Where(m => m.Type == MemoryType.Knowledge) : db.LongMemories.Where(m => m.ProjectKey == key))
                .Where(m => m.Module != null).GroupBy(m => m.Module!).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(cancellationToken);
            foreach (var row in rows) memories[row.Key] = row.Count;

            // A memory of several modules counts for each of them.
            var extra = await (knowledge ? db.LongMemories.Where(m => m.Type == MemoryType.Knowledge) : db.LongMemories.Where(m => m.ProjectKey == key))
                .Where(m => m.Module != null && m.ExtraModules != null).Select(m => m.ExtraModules).ToListAsync(cancellationToken);
            foreach (var name in extra.SelectMany(MemoryLabels.SplitCsv)) memories[name] = memories.GetValueOrDefault(name) + 1;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogStoreWarning(ex, "Could not count memories per module");
        }
        foreach (var record in await ShortAsync())
        {
            if (record.Module is null) continue;
            if (knowledge ? record.Type != MemoryType.Knowledge : record.ProjectKey != key) continue;
            foreach (var name in MemoryLabels.AllModules(record.Module, MemoryLabels.SplitCsv(record.ExtraModules)))
                memories[name] = memories.GetValueOrDefault(name) + 1;
        }

        return modules.Select(m => new ProjectModuleDto(m.Id, m.Name, MemoryLabels.SplitCsv(m.Aliases), m.Description,
            memories.GetValueOrDefault(m.Name), files.GetValueOrDefault(m.Name))).ToList();
    }
}

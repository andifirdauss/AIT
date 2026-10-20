using System.Text;
using AiThread.Common.Entities;
using AiThread.Common.Enums;
using AiThread.Common.Memory;
using AiThread.Data.Memory;
using Microsoft.EntityFrameworkCore;

namespace AiThread.Service.Memory;

public sealed partial class ModuleMerger
{
    private async Task<string> InputAsync(string key, IReadOnlyList<ProjectModule> modules, IReadOnlyList<string>? focus, CancellationToken cancellationToken)
    {
        var files = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var labels = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        try
        {
            await using (var catalog = await longStore.CreateCatalogAsync(cancellationToken))
            {
                var rows = await catalog.CodeMapEntries.AsNoTracking().Where(e => e.ProjectKey == key)
                    .OrderBy(e => e.Path).Select(e => new { e.Module, e.Path }).Take(3000).ToListAsync(cancellationToken);
                foreach (var group in rows.GroupBy(r => r.Module)) files[group.Key] = group.Take(4).Select(r => r.Path).ToList();
            }
            await using var db = await longStore.CreateAsync(cancellationToken);
            var knowledge = key == MemoryLabels.KnowledgeKey;
            var memories = await (knowledge ? db.LongMemories.Where(m => m.Type == MemoryType.Knowledge) : db.LongMemories.Where(m => m.ProjectKey == key))
                .Where(m => m.Module != null).OrderByDescending(m => m.HitCount).Select(m => new { m.Module, m.Label }).Take(1000).ToListAsync(cancellationToken);
            foreach (var group in memories.GroupBy(m => m.Module!)) labels[group.Key] = group.Take(3).Select(m => m.Label).ToList();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogStoreWarning(ex, "Could not read module samples");
        }

        var builder = new StringBuilder();
        builder.Append("# Check\n").Append(focus is null ? "all" : string.Join(", ", focus)).Append("\n\n# Modules\n");
        foreach (var module in modules)
        {
            builder.Append("## ").Append(module.Name).Append('\n');
            if (!string.IsNullOrEmpty(module.Aliases)) builder.Append("aliases: ").Append(module.Aliases.Replace(",", ", ")).Append('\n');
            if (!string.IsNullOrEmpty(module.Description)) builder.Append("description: ").Append(module.Description).Append('\n');
            if (files.TryGetValue(module.Name, out var paths)) builder.Append("files: ").Append(string.Join(", ", paths)).Append('\n');
            if (labels.TryGetValue(module.Name, out var memoryLabels)) builder.Append("memories: ").Append(string.Join(" / ", memoryLabels)).Append('\n');
        }
        return builder.ToString();
    }
}

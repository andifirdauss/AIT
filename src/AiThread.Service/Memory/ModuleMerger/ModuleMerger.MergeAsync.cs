using System.Text.Json;
using AiThread.Common.Entities;
using AiThread.Common.Enums;
using AiThread.Common.Memory;
using AiThread.Service.Interfaces;
using AiThread.Service.Models;
using Microsoft.EntityFrameworkCore;

namespace AiThread.Service.Memory;

public sealed partial class ModuleMerger
{
    public async Task<ModuleMergeLog> MergeAsync(string key, string from, string into, string? reason, double? confidence, string decidedBy,
        CancellationToken cancellationToken, Guid? suggestionId = null)
    {
        var modules = await registry.ListAsync(key, includeMerged: true, cancellationToken);
        var source = modules.FirstOrDefault(m => m.MergedInto is null && string.Equals(m.Name, from, StringComparison.OrdinalIgnoreCase))
                     ?? throw new AppValidationException($"Modul '{from}' tidak ditemukan.");
        var targetName = ModuleRegistry.Resolve(modules, into) ?? throw new AppValidationException($"Modul '{into}' tidak ditemukan.");
        if (string.Equals(source.Name, targetName, StringComparison.OrdinalIgnoreCase))
            throw new AppValidationException("Modul asal dan tujuan sama.");

        var moves = new ModuleMergeMoves([], [], [], [], [], []);
        var knowledge = key == MemoryLabels.KnowledgeKey;

        // Registry: the source becomes a redirect, its names aliases of the target.
        await using (var catalog = await longStore.CreateCatalogAsync(cancellationToken))
        {
            var sourceRow = await catalog.ProjectModules.FirstAsync(m => m.Id == source.Id, cancellationToken);
            var targetRow = await catalog.ProjectModules.FirstAsync(m => m.ProjectKey == key && m.Name == targetName, cancellationToken);
            var before = MemoryLabels.SplitCsv(targetRow.Aliases).ToHashSet(StringComparer.OrdinalIgnoreCase);
            ModuleRegistry.MergeAliases(targetRow, MemoryLabels.SplitCsv(sourceRow.Aliases).Prepend(sourceRow.Name));
            moves.AddedAliases.AddRange(MemoryLabels.SplitCsv(targetRow.Aliases).Where(a => !before.Contains(a)));
            targetRow.UpdatedAt = DateTime.UtcNow;
            if (string.IsNullOrEmpty(targetRow.Description)) targetRow.Description = sourceRow.Description;
            sourceRow.MergedInto = targetRow.Name;
            sourceRow.UpdatedAt = DateTime.UtcNow;

            var sourceName = sourceRow.Name;
            var mapRows = await catalog.CodeMapEntries.Where(e => e.ProjectKey == key && e.Module == sourceName).ToListAsync(cancellationToken);
            foreach (var row in mapRows)
            {
                row.Module = targetRow.Name;
                row.UpdatedAt = DateTime.UtcNow;
                moves.CodeMap.Add(row.Id);
            }
            await catalog.SaveChangesAsync(cancellationToken);
        }

        await MoveLongAsync(key, knowledge, source.Name, targetName, null, moves.LongMemories, cancellationToken);
        await MoveShortAsync(key, knowledge, source.Name, targetName, null, moves.ShortMemories);
        if (!knowledge) await MovePagesAsync(key, source.Name, targetName, moves, cancellationToken);

        ModuleMergeLog log;
        await using (var catalog = await longStore.CreateCatalogAsync(cancellationToken))
        {
            log = suggestionId is { } id ? await catalog.ModuleMergeLogs.FirstOrDefaultAsync(l => l.Id == id, cancellationToken) ?? NewLog() : NewLog();
            if (log.Id != suggestionId) catalog.ModuleMergeLogs.Add(log);
            log.FromModule = source.Name;
            log.IntoModule = targetName;
            log.Status = ModuleMergeStatus.Applied;
            log.Reason = Clip(reason ?? log.Reason, 1000);
            log.Confidence = confidence ?? log.Confidence;
            log.DecidedBy = Clip(decidedBy, 200);
            log.DecidedAt = DateTime.UtcNow;
            log.Moved = JsonSerializer.Serialize(moves, Json);
            await catalog.SaveChangesAsync(cancellationToken);
        }

        var note = moves.SkippedPages.Count > 0 ? $" {moves.SkippedPages.Count} halaman wiki tidak dipindah karena judulnya sudah ada di {targetName}." : string.Empty;
        activityLog.Info("memory", $"Modul {source.Name} digabung ke {targetName} ({decidedBy}): {moves.LongMemories.Count + moves.ShortMemories.Count} memory, " +
                                   $"{moves.CodeMap.Count} file, {moves.Pages.Count} halaman wiki.{note}");
        Publish("merged", key);
        return log;

        ModuleMergeLog NewLog() => new() { Id = Guid.NewGuid(), ProjectKey = key, CreatedAt = DateTime.UtcNow };
    }
}

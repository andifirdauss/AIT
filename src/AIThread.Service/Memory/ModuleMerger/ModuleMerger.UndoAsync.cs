using System.Text.Json;
using AIThread.Common.Entities;
using AIThread.Common.Enums;
using AIThread.Common.Memory;
using AIThread.Service.Interfaces;
using AIThread.Service.Models;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Memory;

public sealed partial class ModuleMerger
{
    public async Task UndoAsync(Guid logId, string decidedBy, CancellationToken cancellationToken)
    {
        ModuleMergeLog log;
        ModuleMergeMoves moves;
        await using (var catalog = await longStore.CreateCatalogAsync(cancellationToken))
        {
            log = await catalog.ModuleMergeLogs.FirstOrDefaultAsync(l => l.Id == logId, cancellationToken)
                  ?? throw new AppValidationException("Riwayat merge tidak ditemukan.");
            if (log.Status != ModuleMergeStatus.Applied) throw new AppValidationException("Hanya merge yang sudah diterapkan yang bisa dibatalkan.");
            moves = JsonSerializer.Deserialize<ModuleMergeMoves>(log.Moved ?? "{}", Json) ?? new ModuleMergeMoves([], [], [], [], [], []);

            var key = log.ProjectKey;
            var from = log.FromModule;
            var into = log.IntoModule;
            var source = await catalog.ProjectModules.FirstOrDefaultAsync(m => m.ProjectKey == key && m.Name == from, cancellationToken);
            if (source is not null)
            {
                source.MergedInto = null;
                source.UpdatedAt = DateTime.UtcNow;
            }
            var target = await catalog.ProjectModules.FirstOrDefaultAsync(m => m.ProjectKey == key && m.Name == into, cancellationToken);
            if (target is not null && moves.AddedAliases.Count > 0)
            {
                var removed = moves.AddedAliases.ToHashSet(StringComparer.OrdinalIgnoreCase);
                target.Aliases = MemoryLabels.JoinCsv(MemoryLabels.SplitCsv(target.Aliases).Where(a => !removed.Contains(a)));
                target.UpdatedAt = DateTime.UtcNow;
            }

            var mapIds = moves.CodeMap;
            var mapRows = await catalog.CodeMapEntries.Where(e => mapIds.Contains(e.Id) && e.Module == into).ToListAsync(cancellationToken);
            foreach (var row in mapRows)
            {
                row.Module = from;
                row.UpdatedAt = DateTime.UtcNow;
            }

            log.Status = ModuleMergeStatus.Undone;
            log.DecidedBy = Clip(decidedBy, 200);
            log.DecidedAt = DateTime.UtcNow;
            await catalog.SaveChangesAsync(cancellationToken);
        }

        var knowledge = log.ProjectKey == MemoryLabels.KnowledgeKey;
        await MoveLongAsync(log.ProjectKey, knowledge, log.IntoModule, log.FromModule, moves.LongMemories.ToHashSet(), [], cancellationToken);
        await MoveShortAsync(log.ProjectKey, knowledge, log.IntoModule, log.FromModule, moves.ShortMemories.ToHashSet(), []);
        if (moves.Pages.Count > 0)
        {
            await using var business = await businessStore.CreateAsync(cancellationToken);
            var ids = moves.Pages;
            var into = log.IntoModule;
            var from = log.FromModule;
            await business.BusinessPages.Where(p => ids.Contains(p.Id) && p.Module == into)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.Module, from), cancellationToken);
        }

        activityLog.Info("memory", $"Merge modul {log.FromModule} → {log.IntoModule} dibatalkan oleh {decidedBy}.");
        Publish("unmerged", log.ProjectKey);
    }
}

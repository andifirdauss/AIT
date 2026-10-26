using AIThread.Common.Localization;
using AIThread.Common.Memory;
using AIThread.Service.Interfaces;
using AIThread.Service.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.Memory;

public sealed partial class ModuleMerger
{
    // Gives a module another name, or only another casing ("Klaimdf" → "KlaimDF"): the registry, redirects of merged
    // modules, merge history, code map, memories (main or extra module) and business pages follow. A different name
    // keeps the old one as an alias, so a model that still writes it lands on the renamed module. Returns the new name.
    public async Task<string> RenameAsync(string key, string name, string newName, string decidedBy, CancellationToken cancellationToken)
    {
        var target = MemoryLabels.Module(newName) ?? throw new AppValidationException(Lang.T("Nama modul baru wajib diisi.", "The new module name is required."));
        string from;
        await using (var catalog = await longStore.CreateCatalogAsync(cancellationToken))
        {
            var modules = await catalog.ProjectModules.Where(m => m.ProjectKey == key).ToListAsync(cancellationToken);
            var source = modules.FirstOrDefault(m => m.MergedInto is null && string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase))
                         ?? throw new AppValidationException(Lang.T($"Modul '{name}' tidak ditemukan.", $"Module '{name}' not found."));
            from = source.Name;
            if (from == target) return target;

            // SameName also covers case, separators and plurals, so the new name cannot shadow another module.
            var compact = MemoryLabels.Compact(target);
            if (modules.FirstOrDefault(m => m.Id != source.Id && (MemoryLabels.SameName(m.Name, target)
                    || MemoryLabels.SplitCsv(m.Aliases).Any(a => MemoryLabels.SameName(a, target)))) is { } taken)
                throw new AppValidationException(Lang.T($"Nama '{target}' sudah dipakai modul {taken.MergedInto ?? taken.Name}. Gabungkan lewat Merge bila keduanya sama.", $"The name '{target}' is already used by module {taken.MergedInto ?? taken.Name}. Merge them with Merge when they are the same."));

            source.Name = target;
            source.Aliases = MemoryLabels.JoinCsv(MemoryLabels.SplitCsv(source.Aliases).Where(a => !string.Equals(a, target, StringComparison.OrdinalIgnoreCase)));
            if (MemoryLabels.Compact(from) != compact) ModuleRegistry.MergeAliases(source, [from]);
            source.UpdatedAt = DateTime.UtcNow;
            foreach (var redirect in modules.Where(m => m.MergedInto == from)) redirect.MergedInto = target;

            var logs = await catalog.ModuleMergeLogs.Where(l => l.ProjectKey == key && (l.FromModule == from || l.IntoModule == from)).ToListAsync(cancellationToken);
            foreach (var log in logs)
            {
                if (log.FromModule == from) log.FromModule = target;
                if (log.IntoModule == from) log.IntoModule = target;
            }

            var mapRows = await catalog.CodeMapEntries.Where(e => e.ProjectKey == key && e.Module == from).ToListAsync(cancellationToken);
            foreach (var row in mapRows)
            {
                row.Module = target;
                row.UpdatedAt = DateTime.UtcNow;
            }
            await catalog.SaveChangesAsync(cancellationToken);
        }

        var knowledge = key == MemoryLabels.KnowledgeKey;
        List<Guid> memories = [];
        await MoveLongAsync(key, knowledge, from, target, null, memories, cancellationToken);
        await MoveShortAsync(key, knowledge, from, target, null, memories);
        if (!knowledge)
        {
            try
            {
                await using var business = await businessStore.CreateAsync(cancellationToken);
                await business.BusinessPages.Where(p => p.ProjectKey == key && p.Module == from)
                    .ExecuteUpdateAsync(s => s.SetProperty(p => p.Module, target), cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Could not rename the module of business pages from {From} to {Into}.", from, target);
            }
        }

        activityLog.Info("memory", Lang.T($"Modul {from} diganti nama menjadi {target} ({decidedBy}): {memories.Count} memory ikut diperbarui.", $"Module {from} renamed to {target} ({decidedBy}): {memories.Count} memories updated along."));
        Publish("renamed", key);
        return target;
    }
}

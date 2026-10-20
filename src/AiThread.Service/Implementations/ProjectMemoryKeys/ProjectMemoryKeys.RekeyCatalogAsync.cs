using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AiThread.Service.Implementations;

public sealed partial class ProjectMemoryKeys
{
    // The module list, code map and merge log move with the memory. Modules or files the new key already has stay.
    private async Task RekeyCatalogAsync(string oldKey, string newKey, CancellationToken cancellationToken)
    {
        try
        {
            await using var catalog = await longStore.CreateCatalogAsync(cancellationToken);
            var taken = (await catalog.ProjectModules.AsNoTracking().Where(m => m.ProjectKey == newKey).Select(m => m.Name).ToListAsync(cancellationToken))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var modules = await catalog.ProjectModules.Where(m => m.ProjectKey == oldKey).ToListAsync(cancellationToken);
            foreach (var module in modules)
            {
                if (taken.Contains(module.Name)) catalog.ProjectModules.Remove(module);
                else module.ProjectKey = newKey;
            }

            var takenPaths = (await catalog.CodeMapEntries.AsNoTracking().Where(e => e.ProjectKey == newKey).Select(e => e.Path).ToListAsync(cancellationToken))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var entries = await catalog.CodeMapEntries.Where(e => e.ProjectKey == oldKey).ToListAsync(cancellationToken);
            foreach (var entry in entries)
            {
                if (takenPaths.Contains(entry.Path)) catalog.CodeMapEntries.Remove(entry);
                else entry.ProjectKey = newKey;
            }
            await catalog.SaveChangesAsync(cancellationToken);

            await catalog.ModuleMergeLogs.Where(l => l.ProjectKey == oldKey)
                .ExecuteUpdateAsync(s => s.SetProperty(l => l.ProjectKey, newKey), cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not move the module list and code map from {OldKey} to {NewKey}.", oldKey, newKey);
        }
    }
}

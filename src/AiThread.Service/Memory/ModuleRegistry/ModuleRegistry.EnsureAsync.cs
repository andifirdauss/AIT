using AiThread.Common.Entities;
using AiThread.Common.Memory;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AiThread.Service.Memory;

public sealed partial class ModuleRegistry
{
    // The module for a name, creating it when the key has none that matches. New aliases are added to an existing one.
    public async Task<(string Name, bool Created)> EnsureAsync(string key, string name, string? description = null,
        IEnumerable<string>? aliases = null, CancellationToken cancellationToken = default)
    {
        var normalized = MemoryLabels.Module(name) ?? CoreModule;
        var aliasList = (aliases ?? []).Select(MemoryLabels.Module).OfType<string>().Where(a => a.Length > 1).ToList();

        for (var attempt = 0; attempt < 2; attempt++)
        {
            await using var db = await store.CreateCatalogAsync(cancellationToken);
            var modules = await db.ProjectModules.Where(m => m.ProjectKey == key).ToListAsync(cancellationToken);
            if (Resolve(modules, normalized) is { } existing)
            {
                var module = modules.First(m => m.Name == existing);
                if (MergeAliases(module, aliasList.Append(normalized).Where(a => !string.Equals(a, module.Name, StringComparison.OrdinalIgnoreCase))))
                {
                    module.UpdatedAt = DateTime.UtcNow;
                    await db.SaveChangesAsync(cancellationToken);
                }
                return (existing, false);
            }

            var created = new ProjectModule
            {
                Id = Guid.NewGuid(),
                ProjectKey = key,
                Name = normalized,
                Description = Clip(description?.Trim(), 500),
                CreatedAt = DateTime.UtcNow
            };
            MergeAliases(created, aliasList.Where(a => a != normalized));
            db.ProjectModules.Add(created);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
                _created.Enqueue((key, normalized));
                _signal.Release();
                return (normalized, true);
            }
            catch (DbUpdateException ex) when (attempt == 0)
            {
                // Another developer's AiThread created it meanwhile (unique key + name): read it again.
                logger.LogDebug(ex, "Module {Module} of {Key} was created concurrently.", normalized, key);
            }
        }
        return (normalized, false);
    }
}

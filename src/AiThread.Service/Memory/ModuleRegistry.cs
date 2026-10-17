using System.Collections.Concurrent;
using AiThread.Common.Entities;
using AiThread.Common.Memory;
using AiThread.Data.Memory;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AiThread.Service.Memory;

// The modules of every project key (and of knowledge memory under MemoryLabels.KnowledgeKey). A name is resolved by
// name or alias ignoring case and separators, then by English plural; a merged module redirects to the module it was
// merged into. New modules are queued for the duplicate check (MemoryRelabelWorker drains the queue).
public sealed class ModuleRegistry(LongMemoryStore store, ILogger<ModuleRegistry> logger)
{
    public const string CoreModule = "Core";

    private readonly ConcurrentQueue<(string Key, string Module)> _created = new();
    private readonly SemaphoreSlim _signal = new(0);

    // Active modules (not merged) of the key; includeMerged also returns the redirects.
    public async Task<IReadOnlyList<ProjectModule>> ListAsync(string key, bool includeMerged = false, CancellationToken cancellationToken = default)
    {
        await using var db = await store.CreateCatalogAsync(cancellationToken);
        var query = db.ProjectModules.AsNoTracking().Where(m => m.ProjectKey == key);
        if (!includeMerged) query = query.Where(m => m.MergedInto == null);
        return await query.OrderBy(m => m.Name).ToListAsync(cancellationToken);
    }

    // The active module a name stands for, or null when the key has no such module. `modules` must include the merged
    // ones (redirects) for old names to resolve.
    public static string? Resolve(IReadOnlyList<ProjectModule> modules, string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var compact = MemoryLabels.Compact(name);
        if (compact.Length == 0) return null;

        var match = modules.FirstOrDefault(m => MemoryLabels.Compact(m.Name) == compact)
                    ?? modules.FirstOrDefault(m => MemoryLabels.SplitCsv(m.Aliases).Any(a => MemoryLabels.Compact(a) == compact))
                    ?? modules.FirstOrDefault(m => MemoryLabels.SameName(m.Name, name))
                    ?? modules.FirstOrDefault(m => MemoryLabels.SplitCsv(m.Aliases).Any(a => MemoryLabels.SameName(a, name)));

        // Follow merges; a broken or looping chain ends at the last module found.
        for (var hops = 0; match?.MergedInto is { } next && hops < 8; hops++)
            match = modules.FirstOrDefault(m => string.Equals(m.Name, next, StringComparison.OrdinalIgnoreCase)) ?? match;
        return match is null ? null : match.MergedInto ?? match.Name;
    }

    public async Task<string?> ResolveAsync(string key, string? name, CancellationToken cancellationToken = default) =>
        Resolve(await ListAsync(key, includeMerged: true, cancellationToken), name);

    // The module for a name, creating it when the key has none that matches. New aliases are added to an existing one.
    public async Task<(string Name, bool Created)> EnsureAsync(string key, string name, string? description = null,
        IEnumerable<string>? aliases = null, CancellationToken cancellationToken = default)
    {
        var normalized = MemoryLabels.Module(name) ?? CoreModule;
        var aliasList = (aliases ?? []).Select(MemoryLabels.Pascal).Where(a => a.Length > 1).ToList();

        for (var attempt = 0; attempt < 2; attempt++)
        {
            await using var db = await store.CreateCatalogAsync(cancellationToken);
            var modules = await db.ProjectModules.Where(m => m.ProjectKey == key).ToListAsync(cancellationToken);
            if (Resolve(modules, normalized) is { } existing)
            {
                var module = modules.First(m => m.Name == existing);
                if (MergeAliases(module, aliasList.Append(normalized).Where(a => a != module.Name)))
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

    public async Task<ProjectModule?> UpdateAsync(string key, string name, string? description, IReadOnlyList<string>? aliases, CancellationToken cancellationToken)
    {
        await using var db = await store.CreateCatalogAsync(cancellationToken);
        var module = await db.ProjectModules.FirstOrDefaultAsync(m => m.ProjectKey == key && m.Name == name, cancellationToken);
        if (module is null) return null;
        if (description is not null) module.Description = Clip(description.Trim(), 500);
        if (aliases is not null)
        {
            module.Aliases = null;
            MergeAliases(module, aliases.Select(MemoryLabels.Pascal).Where(a => a.Length > 1 && a != module.Name));
        }
        module.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return module;
    }

    // Modules created since the last call, per key.
    public IReadOnlyDictionary<string, IReadOnlyList<string>> DrainCreated()
    {
        var result = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        while (_created.TryDequeue(out var item))
        {
            if (!result.TryGetValue(item.Key, out var list)) result[item.Key] = list = [];
            if (!list.Contains(item.Module)) list.Add(item.Module);
        }
        return result.ToDictionary(r => r.Key, r => (IReadOnlyList<string>)r.Value);
    }

    // Returns early (true) when a module is created, so the duplicate check runs soon after. Several creations wake
    // the caller once.
    public async Task<bool> WaitForCreatedAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        var woken = await _signal.WaitAsync(timeout, cancellationToken);
        while (_signal.CurrentCount > 0) _signal.Wait(0, CancellationToken.None);
        return woken;
    }

    // True when an alias was added.
    public static bool MergeAliases(ProjectModule module, IEnumerable<string> aliases)
    {
        var current = MemoryLabels.SplitCsv(module.Aliases).ToList();
        var changed = false;
        foreach (var alias in aliases)
        {
            if (string.IsNullOrWhiteSpace(alias) || current.Any(a => string.Equals(a, alias, StringComparison.OrdinalIgnoreCase))) continue;
            if (string.Join(",", current.Append(alias)).Length > 1000) break;
            current.Add(alias);
            changed = true;
        }
        if (changed) module.Aliases = MemoryLabels.JoinCsv(current);
        return changed;
    }

    private static string? Clip(string? value, int max) =>
        string.IsNullOrEmpty(value) ? null : value.Length > max ? value[..max] : value;
}

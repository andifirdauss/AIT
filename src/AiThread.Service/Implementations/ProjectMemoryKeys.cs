using AiThread.Common.Entities;
using AiThread.Common.Enums;
using AiThread.Data.Cache;
using AiThread.Data.Memory;
using AiThread.Service.Workspace;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AiThread.Service.Implementations;

// Keeps the ProjectKey of stored memory in line with the projects registered on this machine. The stores may be
// shared by several developers, so this only ever touches rows this machine owns (its project ids, or a local:
// key that no other machine can have) unless the user explicitly asks for more.
public sealed class ProjectMemoryKeys(
    IShortMemoryStore shortStore,
    LongMemoryStore longStore,
    ILogger<ProjectMemoryKeys> logger)
{
    // A store is shared when it is an external database or server; the built-in ones live on this machine only.
    public bool ShortStoreIsShared => shortStore is SwitchableShortMemoryStore { Connection.Provider: not MemoryStoreProvider.Default };

    public bool LongStoreIsShared => longStore.IsExternal;

    // Fills ProjectKey/ProjectName of rows written before keys existed. Idempotent; every developer's AiThread runs
    // it for its own projects, so a shared store ends up fully keyed.
    public async Task<int> BackfillAsync(IReadOnlyList<Project> projects, CancellationToken cancellationToken)
    {
        var keyed = projects.Where(p => !string.IsNullOrEmpty(p.ProjectKey)).ToDictionary(p => p.Id);
        if (keyed.Count == 0) return 0;

        var updated = 0;
        try
        {
            await using var db = await longStore.CreateAsync(cancellationToken);
            foreach (var project in keyed.Values)
            {
                var key = project.ProjectKey;
                var name = project.Name;
                var id = project.Id;
                updated += await db.LongMemories
                    .Where(m => m.ProjectKey == null && m.ProjectId == id && m.Type != MemoryType.Knowledge)
                    .ExecuteUpdateAsync(s => s.SetProperty(m => m.ProjectKey, key).SetProperty(m => m.ProjectName, name), cancellationToken);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogStoreWarning(ex, "Could not fill the project keys of long memories");
        }

        updated += await UpdateShortAsync(
            r => r.ProjectKey is null && r.Type != MemoryType.Knowledge && r.ProjectId is { } id && keyed.ContainsKey(id),
            r => r with { ProjectKey = keyed[r.ProjectId!.Value].ProjectKey, ProjectName = keyed[r.ProjectId!.Value].Name });
        return updated;
    }

    // A project whose key changed from its machine-only fallback (a remote, an override file or a manual key was
    // added) brings that memory along. A shared key is never rewritten: other developers' clones still use it.
    public async Task<int> RekeyAsync(string oldKey, string newKey, string projectName, CancellationToken cancellationToken)
    {
        if (oldKey == newKey || !ProjectKeyResolver.IsLocal(oldKey)) return 0;

        var updated = 0;
        try
        {
            await using var db = await longStore.CreateAsync(cancellationToken);
            updated += await db.LongMemories
                .Where(m => m.ProjectKey == oldKey)
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.ProjectKey, newKey).SetProperty(m => m.ProjectName, projectName), cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not move long memories from {OldKey} to {NewKey}.", oldKey, newKey);
        }

        updated += await UpdateShortAsync(r => r.ProjectKey == oldKey, r => r with { ProjectKey = newKey, ProjectName = projectName });
        await RekeyCatalogAsync(oldKey, newKey, cancellationToken);
        return updated;
    }

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

    // Removes the memory of a project being deleted. Memory in a shared store belongs to every developer of the
    // project and is kept unless includeShared; a machine-only key is never shared, wherever it is stored.
    public async Task<int> DeleteAsync(Guid projectId, string projectKey, bool includeShared, CancellationToken cancellationToken)
    {
        var localKey = string.IsNullOrEmpty(projectKey) || ProjectKeyResolver.IsLocal(projectKey);
        var key = string.IsNullOrEmpty(projectKey) ? null : projectKey;
        var deleted = 0;

        if (!ShortStoreIsShared || includeShared || localKey)
            deleted += await DeleteShortAsync(r => (key is not null && r.ProjectKey == key) || (r.ProjectKey is null && r.ProjectId == projectId));

        if (!LongStoreIsShared || includeShared || localKey)
        {
            try
            {
                await using var db = await longStore.CreateAsync(cancellationToken);
                deleted += await db.LongMemories
                    .Where(m => (key != null && m.ProjectKey == key) || (m.ProjectKey == null && m.ProjectId == projectId))
                    .ExecuteDeleteAsync(cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Could not delete long memories of project {ProjectId}", projectId);
            }

            // The module list and code map go with the memory they describe.
            if (key is not null)
            {
                try
                {
                    await using var catalog = await longStore.CreateCatalogAsync(cancellationToken);
                    await catalog.CodeMapEntries.Where(e => e.ProjectKey == key).ExecuteDeleteAsync(cancellationToken);
                    await catalog.ModuleMergeLogs.Where(l => l.ProjectKey == key).ExecuteDeleteAsync(cancellationToken);
                    await catalog.ProjectModules.Where(m => m.ProjectKey == key).ExecuteDeleteAsync(cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning(ex, "Could not delete the module list and code map of project {ProjectId}", projectId);
                }
            }
        }
        return deleted;
    }

    // Another clone of the same repository stays registered: long memory in the main database follows it instead of
    // being removed with the project by the foreign key.
    public async Task HandOverAsync(Guid fromProjectId, Guid toProjectId, CancellationToken cancellationToken)
    {
        if (LongStoreIsShared) return;
        await using var db = await longStore.CreateAsync(cancellationToken);
        await db.LongMemories
            .Where(m => m.ProjectId == fromProjectId)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.ProjectId, toProjectId), cancellationToken);
    }

    private async Task<int> UpdateShortAsync(Func<ShortMemoryRecord, bool> match, Func<ShortMemoryRecord, ShortMemoryRecord> update)
    {
        if (!shortStore.IsConnected) return 0;
        try
        {
            var records = (await shortStore.GetAllAsync()).Where(match).ToList();
            foreach (var record in records) await shortStore.SaveAsync(update(record));
            return records.Count;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not update the project keys of short memories.");
            return 0;
        }
    }

    private async Task<int> DeleteShortAsync(Func<ShortMemoryRecord, bool> match)
    {
        if (!shortStore.IsConnected) return 0;
        try
        {
            var records = (await shortStore.GetAllAsync()).Where(match).ToList();
            foreach (var record in records) await shortStore.DeleteAsync(record.Id);
            return records.Count;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not delete short memories.");
            return 0;
        }
    }
}

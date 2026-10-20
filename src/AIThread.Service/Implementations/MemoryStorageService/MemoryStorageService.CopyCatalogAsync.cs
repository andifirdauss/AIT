using AIThread.Common.Entities;
using AIThread.Data.Memory;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.Implementations;

public sealed partial class MemoryStorageService
{
    // The module list, code map and merge log follow long memory. What the new store already has (same key and name,
    // same key and path, same id) is left alone, so another developer's catalog there wins.
    private async Task CopyCatalogAsync(MemoryStoreConnection from, MemoryStoreConnection to, List<string> warnings, CancellationToken cancellationToken)
    {
        try
        {
            List<ProjectModule> modules;
            List<CodeMapEntry> entries;
            List<ModuleMergeLog> merges;
            await using (var source = await longStore.OpenCatalogAsync(from, cancellationToken))
            {
                modules = await source.ProjectModules.AsNoTracking().ToListAsync(cancellationToken);
                entries = await source.CodeMapEntries.AsNoTracking().ToListAsync(cancellationToken);
                merges = await source.ModuleMergeLogs.AsNoTracking().ToListAsync(cancellationToken);
            }
            if (modules.Count + entries.Count + merges.Count == 0) return;

            await using var target = await longStore.OpenCatalogAsync(to, cancellationToken);
            var moduleKeys = (await target.ProjectModules.AsNoTracking().Select(m => new { m.ProjectKey, m.Name }).ToListAsync(cancellationToken))
                .Select(m => m.ProjectKey + "|" + m.Name.ToLowerInvariant()).ToHashSet();
            var entryKeys = (await target.CodeMapEntries.AsNoTracking().Select(e => new { e.ProjectKey, e.Path }).ToListAsync(cancellationToken))
                .Select(e => e.ProjectKey + "|" + e.Path.ToLowerInvariant()).ToHashSet();
            var mergeIds = (await target.ModuleMergeLogs.AsNoTracking().Select(l => l.Id).ToListAsync(cancellationToken)).ToHashSet();

            target.ProjectModules.AddRange(modules.Where(m => moduleKeys.Add(m.ProjectKey + "|" + m.Name.ToLowerInvariant())));
            target.ModuleMergeLogs.AddRange(merges.Where(l => mergeIds.Add(l.Id)));
            await target.SaveChangesAsync(cancellationToken);
            foreach (var batch in entries.Where(e => entryKeys.Add(e.ProjectKey + "|" + e.Path.ToLowerInvariant())).Chunk(CopyBatchSize))
            {
                target.CodeMapEntries.AddRange(batch);
                await target.SaveChangesAsync(cancellationToken);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "The module list and code map could not be copied to the new store.");
            warnings.Add($"Daftar modul dan peta kode tidak dapat disalin (akan dibangun ulang otomatis): {MemoryStoreTester.ErrorMessage(ex)}");
        }
    }
}

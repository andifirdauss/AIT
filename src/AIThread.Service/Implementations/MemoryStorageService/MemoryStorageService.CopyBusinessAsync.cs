using AIThread.Common.Localization;
using AIThread.Data.Memory;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.Implementations;

public sealed partial class MemoryStorageService
{
    // Inserts the pages, revisions and images the new store does not have yet (by id). A page whose project key and
    // slug already exist there (the same wiki written by another developer) is left out, with its revisions.
    private async Task<int> CopyBusinessAsync(MemoryStoreConnection from, MemoryStoreConnection to, List<string> warnings, CancellationToken cancellationToken)
    {
        try
        {
            await using var source = await businessStore.OpenAsync(from, cancellationToken);
            await using var target = await businessStore.OpenAsync(to, cancellationToken);

            var pages = await source.BusinessPages.AsNoTracking().ToListAsync(cancellationToken);
            if (pages.Count == 0) return 0;

            var existingIds = (await target.BusinessPages.AsNoTracking().Select(p => p.Id).ToListAsync(cancellationToken)).ToHashSet();
            var existingSlugs = (await target.BusinessPages.AsNoTracking().Select(p => new { p.ProjectKey, p.Slug }).ToListAsync(cancellationToken))
                .Select(p => (p.ProjectKey, p.Slug)).ToHashSet();
            var copy = pages.Where(p => !existingIds.Contains(p.Id) && !existingSlugs.Contains((p.ProjectKey, p.Slug))).ToList();
            var skipped = pages.Count(p => !existingIds.Contains(p.Id)) - copy.Count;
            if (skipped > 0) warnings.Add(Lang.T($"{skipped} halaman bisnis tidak disalin karena store baru sudah punya halaman dengan judul yang sama.", $"{skipped} business pages were not copied because the new store already has a page with the same title."));

            foreach (var batch in copy.Chunk(CopyBatchSize))
            {
                var ids = batch.Select(p => p.Id).ToList();
                target.BusinessPages.AddRange(batch);
                target.BusinessPageRevisions.AddRange(await source.BusinessPageRevisions.AsNoTracking().Where(r => ids.Contains(r.PageId)).ToListAsync(cancellationToken));
                await target.SaveChangesAsync(cancellationToken);
            }

            // Images one at a time: each may be several megabytes.
            var existingAssets = (await target.BusinessAssets.AsNoTracking().Select(a => a.Id).ToListAsync(cancellationToken)).ToHashSet();
            var assetIds = await source.BusinessAssets.AsNoTracking().Select(a => a.Id).ToListAsync(cancellationToken);
            foreach (var assetId in assetIds.Where(id => !existingAssets.Contains(id)))
            {
                var asset = await source.BusinessAssets.AsNoTracking().FirstAsync(a => a.Id == assetId, cancellationToken);
                target.BusinessAssets.Add(asset);
                await target.SaveChangesAsync(cancellationToken);
                target.ChangeTracker.Clear();
            }
            return copy.Count;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Business memory could not be copied to the new store.");
            warnings.Add(Lang.T($"Business memory lama tidak dapat disalin (sebagian mungkin sudah tersalin): {MemoryStoreTester.ErrorMessage(ex)}", $"The old business memory cannot be copied (part of it may already be copied): {MemoryStoreTester.ErrorMessage(ex)}"));
            return 0;
        }
    }
}

using AiThread.Service.Business;
using AiThread.Service.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace AiThread.Service.Implementations;

public sealed partial class BusinessMemoryService
{
    public async Task<bool> DeletePageAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await store.CreateAsync(cancellationToken);
        var page = await db.BusinessPages.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (page is null) return false;

        await db.BusinessPageRevisions.Where(r => r.PageId == id).ExecuteDeleteAsync(cancellationToken);
        await db.BusinessPages.Where(p => p.Id == id).ExecuteDeleteAsync(cancellationToken);

        // Images of the page go with it, unless another page of the project shows them too.
        var key = page.ProjectKey;
        var assets = await db.BusinessAssets.AsNoTracking().Where(a => a.PageId == id).Select(a => a.Id).ToListAsync(cancellationToken);
        foreach (var assetId in assets)
        {
            var reference = BusinessMarkdown.AssetScheme + assetId;
            var used = await db.BusinessPages.AnyAsync(p => p.ProjectKey == key && p.Content.Contains(reference), cancellationToken);
            if (used) await db.BusinessAssets.Where(a => a.Id == assetId).ExecuteUpdateAsync(s => s.SetProperty(a => a.PageId, (Guid?)null), cancellationToken);
            else await db.BusinessAssets.Where(a => a.Id == assetId).ExecuteDeleteAsync(cancellationToken);
        }

        Publish("deleted", page);
        var settings = await settingService.GetRuntimeAsync(cancellationToken);
        activityLog.Info("business", $"Halaman bisnis '{page.Module} — {page.Title}' dihapus oleh {settings.EffectiveDeveloperName}.");
        return true;
    }
}

using AiThread.Common.Entities;
using Microsoft.EntityFrameworkCore;

namespace AiThread.Service.Memory;

public sealed partial class ModuleRegistry
{
    // Active modules (not merged) of the key; includeMerged also returns the redirects.
    public async Task<IReadOnlyList<ProjectModule>> ListAsync(string key, bool includeMerged = false, CancellationToken cancellationToken = default)
    {
        await using var db = await store.CreateCatalogAsync(cancellationToken);
        var query = db.ProjectModules.AsNoTracking().Where(m => m.ProjectKey == key);
        if (!includeMerged) query = query.Where(m => m.MergedInto == null);
        return await query.OrderBy(m => m.Name).ToListAsync(cancellationToken);
    }
}

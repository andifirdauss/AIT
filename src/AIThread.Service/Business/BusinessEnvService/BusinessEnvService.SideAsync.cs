using AIThread.Data.Memory;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Business;

public sealed partial class BusinessEnvService
{
    // The pages of one compare side: the working wiki, an environment, or a remote branch (worked out).
    private async Task<Dictionary<Guid, SidePage>> SideAsync(IBusinessMemoryDb db, string key, string side, CancellationToken cancellationToken)
    {
        if (string.Equals(side, Working, StringComparison.OrdinalIgnoreCase))
            return await db.BusinessPages.AsNoTracking().Where(p => p.ProjectKey == key)
                .ToDictionaryAsync(p => p.Id, p => new SidePage(p.Module, p.Title, p.Version, p.Content), cancellationToken);
        var pages = await IsRegisteredAsync(key, side, cancellationToken)
            ? await db.BusinessEnvPages.AsNoTracking().Where(p => p.ProjectKey == key && p.Environment == side).ToListAsync(cancellationToken)
            : await ComputedPagesAsync(db, key, side, null, cancellationToken);
        return pages.ToDictionary(p => p.PageId, p => new SidePage(p.Module, p.Title, p.Version, p.Content));
    }
}

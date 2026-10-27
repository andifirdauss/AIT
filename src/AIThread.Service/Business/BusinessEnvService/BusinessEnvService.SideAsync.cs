using AIThread.Data.Memory;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Business;

public sealed partial class BusinessEnvService
{
    // The pages of one compare side: the working wiki, or an environment.
    private static async Task<Dictionary<Guid, SidePage>> SideAsync(IBusinessMemoryDb db, string key, string side, CancellationToken cancellationToken)
    {
        if (string.Equals(side, Working, StringComparison.OrdinalIgnoreCase))
            return await db.BusinessPages.AsNoTracking().Where(p => p.ProjectKey == key)
                .ToDictionaryAsync(p => p.Id, p => new SidePage(p.Module, p.Title, p.Version, p.Content), cancellationToken);
        return await db.BusinessEnvPages.AsNoTracking().Where(p => p.ProjectKey == key && p.Environment == side)
            .ToDictionaryAsync(p => p.PageId, p => new SidePage(p.Module, p.Title, p.Version, p.Content), cancellationToken);
    }
}

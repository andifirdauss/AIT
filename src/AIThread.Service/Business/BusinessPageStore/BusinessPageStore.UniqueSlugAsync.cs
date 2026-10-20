using AIThread.Data.Memory;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Business;

public sealed partial class BusinessPageStore
{
    private static async Task<string> UniqueSlugAsync(IBusinessMemoryDb db, string projectKey, string slug, CancellationToken cancellationToken)
    {
        var taken = await db.BusinessPages.AsNoTracking()
            .Where(p => p.ProjectKey == projectKey && p.Slug.StartsWith(slug))
            .Select(p => p.Slug)
            .ToListAsync(cancellationToken);
        if (!taken.Contains(slug)) return slug;
        for (var i = 2; ; i++)
        {
            var candidate = $"{slug}-{i}";
            if (!taken.Contains(candidate)) return candidate;
        }
    }
}

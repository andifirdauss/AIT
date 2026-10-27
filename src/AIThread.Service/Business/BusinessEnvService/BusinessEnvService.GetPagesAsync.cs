using AIThread.Common.DTOs;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Business;

public sealed partial class BusinessEnvService
{
    // Null when the project is not registered.
    public async Task<BusinessEnvOverviewDto?> GetPagesAsync(Guid projectId, string environment, CancellationToken cancellationToken = default)
    {
        if (await KeyAsync(projectId, cancellationToken) is not { Length: > 0 } key) return null;
        await using var db = await store.CreateAsync(cancellationToken);
        var working = await db.BusinessPages.AsNoTracking().Where(p => p.ProjectKey == key)
            .Select(p => new { p.Id, p.Version }).ToDictionaryAsync(p => p.Id, p => p.Version, cancellationToken);
        var pages = await db.BusinessEnvPages.AsNoTracking().Where(p => p.ProjectKey == key && p.Environment == environment)
            .OrderBy(p => p.Module).ThenBy(p => p.Title)
            .Select(p => new BusinessEnvPageDto(p.PageId, p.Environment, p.Module, p.Title, p.Slug, p.Summary, p.Version, null, p.UpdatedAt, null, null))
            .ToListAsync(cancellationToken);
        return new BusinessEnvOverviewDto(environment,
            pages.Select(p => p with { WorkingVersion = working.TryGetValue(p.PageId, out var version) ? version : null }).ToList(),
            working.Keys.Count(id => pages.All(p => p.PageId != id)));
    }
}

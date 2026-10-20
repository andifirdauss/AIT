using AIThread.Common.Entities;
using AIThread.Data.Memory;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Implementations;

public sealed partial class BusinessMemoryService
{
    // Summary columns only: Content may be large.
    private static IQueryable<BusinessPage> SummaryQuery(IBusinessMemoryDb db, string key) =>
        db.BusinessPages.AsNoTracking()
            .Where(p => p.ProjectKey == key)
            .Select(p => new BusinessPage
            {
                Id = p.Id, ProjectKey = p.ProjectKey, ProjectName = p.ProjectName, Module = p.Module, Title = p.Title, Slug = p.Slug,
                Summary = p.Summary, Labels = p.Labels, Version = p.Version, AutoUpdate = p.AutoUpdate, CreatedAt = p.CreatedAt,
                UpdatedAt = p.UpdatedAt, UpdatedBy = p.UpdatedBy, LastEditKind = p.LastEditKind
            });
}

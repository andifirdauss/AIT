using AIThread.Common.DTOs;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Business;

public sealed partial class BusinessEnvService
{
    // The page as it holds in the environment, rendered like a working page; null when the environment has no version.
    public async Task<BusinessEnvPageDto?> GetPageAsync(Guid pageId, string environment, CancellationToken cancellationToken = default)
    {
        await using var db = await store.CreateAsync(cancellationToken);
        var page = await db.BusinessEnvPages.AsNoTracking().FirstOrDefaultAsync(p => p.PageId == pageId && p.Environment == environment, cancellationToken);
        if (page is null) return null;
        var working = await db.BusinessPages.AsNoTracking().Where(p => p.Id == pageId).Select(p => (int?)p.Version).FirstOrDefaultAsync(cancellationToken);
        return new BusinessEnvPageDto(page.PageId, page.Environment, page.Module, page.Title, page.Slug, page.Summary, page.Version, working, page.UpdatedAt,
            page.Content, BusinessMarkdown.ToHtml(page.Content));
    }
}

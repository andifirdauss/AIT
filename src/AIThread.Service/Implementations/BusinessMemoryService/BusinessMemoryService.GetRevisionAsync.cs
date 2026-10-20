using AIThread.Common.DTOs;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Implementations;

public sealed partial class BusinessMemoryService
{
    public async Task<BusinessRevisionDto?> GetRevisionAsync(Guid pageId, int version, CancellationToken cancellationToken = default)
    {
        await using var db = await store.CreateAsync(cancellationToken);
        var r = await db.BusinessPageRevisions.AsNoTracking().FirstOrDefaultAsync(r => r.PageId == pageId && r.Version == version, cancellationToken);
        return r is null ? null : new BusinessRevisionDto(r.Id, r.Version, r.Title, r.ChangeNote, r.Author, r.EditKind, r.CreatedAt, ThreadIds(r.SourceThreadIds), r.Content);
    }
}

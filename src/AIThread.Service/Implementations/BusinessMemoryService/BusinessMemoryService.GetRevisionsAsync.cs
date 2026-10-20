using AIThread.Common.DTOs;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Implementations;

public sealed partial class BusinessMemoryService
{
    public async Task<IReadOnlyList<BusinessRevisionDto>> GetRevisionsAsync(Guid pageId, CancellationToken cancellationToken = default)
    {
        await using var db = await store.CreateAsync(cancellationToken);
        var rows = await db.BusinessPageRevisions.AsNoTracking()
            .Where(r => r.PageId == pageId)
            .OrderByDescending(r => r.Version)
            .Select(r => new { r.Id, r.Version, r.Title, r.ChangeNote, r.Author, r.EditKind, r.CreatedAt, r.SourceThreadIds })
            .ToListAsync(cancellationToken);
        return rows.Select(r => new BusinessRevisionDto(r.Id, r.Version, r.Title, r.ChangeNote, r.Author, r.EditKind, r.CreatedAt, ThreadIds(r.SourceThreadIds))).ToList();
    }
}

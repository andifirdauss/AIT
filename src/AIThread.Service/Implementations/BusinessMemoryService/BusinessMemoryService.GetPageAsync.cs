using AIThread.Common.DTOs;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Implementations;

public sealed partial class BusinessMemoryService
{
    public async Task<BusinessPageDto?> GetPageAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await store.CreateAsync(cancellationToken);
        var page = await db.BusinessPages.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        return page is null ? null : ToDto(page);
    }
}

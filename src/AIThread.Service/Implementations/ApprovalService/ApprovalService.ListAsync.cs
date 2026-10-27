using AIThread.Common.DTOs;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Implementations;

public sealed partial class ApprovalService
{
    public async Task<IReadOnlyList<ApprovalGrantDto>> ListAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.ApprovalGrants.AsNoTracking()
            .Where(g => g.ProjectId == projectId)
            .OrderBy(g => g.Kind).ThenBy(g => g.Target)
            .Select(g => new ApprovalGrantDto(g.Id, g.Kind, g.Target, g.CreatedBy, g.CreatedAt, g.LastUsedAt, g.UseCount))
            .ToListAsync(cancellationToken);
    }
}

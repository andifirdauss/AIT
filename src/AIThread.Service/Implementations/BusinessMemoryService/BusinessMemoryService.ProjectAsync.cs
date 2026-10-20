using AIThread.Common.Entities;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Implementations;

public sealed partial class BusinessMemoryService
{
    private async Task<Project?> ProjectAsync(Guid projectId, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.Projects.AsNoTracking().FirstOrDefaultAsync(p => p.Id == projectId && p.ProjectKey != "", cancellationToken);
    }
}

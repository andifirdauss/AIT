using AIThread.Common.DTOs;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Implementations;

public sealed partial class ProjectDatabaseService
{
    public async Task<IReadOnlyList<ProjectDatabaseDto>> GetAllAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var entities = await db.ProjectDatabases.AsNoTracking().Where(d => d.ProjectId == projectId).OrderBy(d => d.Name).ToListAsync(cancellationToken);
        return entities.Select(ToDto).ToList();
    }
}

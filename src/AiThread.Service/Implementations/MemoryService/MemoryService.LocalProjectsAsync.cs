using Microsoft.EntityFrameworkCore;

namespace AiThread.Service.Implementations;

public sealed partial class MemoryService
{
    private async Task<LocalProjects> LocalProjectsAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var rows = await db.Projects.AsNoTracking()
            .Select(p => new { p.Id, p.ProjectKey, p.Name })
            .ToListAsync(cancellationToken);
        return new LocalProjects(rows.Select(p => new ProjectScope(p.Id, string.IsNullOrEmpty(p.ProjectKey) ? null : p.ProjectKey, p.Name)).ToList());
    }
}

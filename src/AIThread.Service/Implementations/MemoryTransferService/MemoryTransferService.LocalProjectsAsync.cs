using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Implementations;

public sealed partial class MemoryTransferService
{
    private async Task<LocalProjects> LocalProjectsAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var rows = await db.Projects.AsNoTracking().Select(p => new { p.Id, p.ProjectKey, p.Name }).ToListAsync(cancellationToken);
        return new LocalProjects(rows.Select(p => new LocalProject(p.Id, string.IsNullOrEmpty(p.ProjectKey) ? null : p.ProjectKey, p.Name)).ToList());
    }
}

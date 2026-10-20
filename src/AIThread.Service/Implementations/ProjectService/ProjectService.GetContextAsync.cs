using AIThread.Service.Models;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Implementations;

public sealed partial class ProjectService
{
    public async Task<ProjectContext?> GetContextAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == projectId, cancellationToken);
        if (project is null) return null;

        project.LastSeenAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return ToContext(project);
    }
}

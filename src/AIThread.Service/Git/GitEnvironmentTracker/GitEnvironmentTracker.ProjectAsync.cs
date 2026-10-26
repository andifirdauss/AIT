using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Git;

public sealed partial class GitEnvironmentTracker
{
    // The project's key and root folder; null when it is not registered or has no key yet.
    private async Task<(string Key, string Root)?> ProjectAsync(Guid projectId, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var project = await db.Projects.AsNoTracking().Where(p => p.Id == projectId)
            .Select(p => new { p.ProjectKey, p.RootPath }).FirstOrDefaultAsync(cancellationToken);
        return project is null || string.IsNullOrEmpty(project.ProjectKey) ? null : (project.ProjectKey, project.RootPath);
    }
}

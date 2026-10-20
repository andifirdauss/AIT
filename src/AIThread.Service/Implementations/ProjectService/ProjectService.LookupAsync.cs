using AIThread.Common.Entities;
using AIThread.Service.Workspace;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Implementations;

public sealed partial class ProjectService
{
    // A registered project that contains the folder keeps its identity wherever the locator would put the root today
    // (projects registered by hand, or by an earlier version that identified them by their .sln file).
    private async Task<(Project? Project, ProjectResolution? Resolution)> LookupAsync(string? start, CancellationToken cancellationToken)
    {
        if (start is null) return (null, null);

        Project? registered;
        await using (var db = await dbFactory.CreateDbContextAsync(cancellationToken))
        {
            registered = (await db.Projects.AsNoTracking().ToListAsync(cancellationToken))
                .Where(p => IsSameOrInside(start, p.RootPath) && Directory.Exists(p.RootPath))
                .OrderByDescending(p => p.RootPath.Length)
                .FirstOrDefault();
        }
        if (registered is not null) return (await UpsertAsync(registered.RootPath, registered.Name, cancellationToken), null);

        var resolution = ProjectLocator.Resolve(start);
        var project = resolution.Root is { } root ? await UpsertAsync(root.Path, root.Name, cancellationToken) : null;
        return (project, resolution);
    }
}

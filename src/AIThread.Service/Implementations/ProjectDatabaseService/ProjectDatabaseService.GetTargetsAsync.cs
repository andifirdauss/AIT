using AIThread.Service.Databases;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Implementations;

public sealed partial class ProjectDatabaseService
{
    public async Task<IReadOnlyList<DatabaseTarget>> GetTargetsAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var entities = await db.ProjectDatabases.AsNoTracking().Where(d => d.ProjectId == projectId).OrderBy(d => d.Name).ToListAsync(cancellationToken);
        return entities.Select(e => new DatabaseTarget(e.Id, e.ProjectId, e.Name, ToConnection(e, Unprotect(e.PasswordProtected)), e.Environment,
            SensitiveColumns.Parse(e.MaskedColumns), e.MaxRows, e.AllowWrites, e.Description)).ToList();
    }
}

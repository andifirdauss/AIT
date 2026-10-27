using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Business;

public sealed partial class BusinessEnvService
{
    // The project's key; null when it is not registered.
    private async Task<string?> KeyAsync(Guid projectId, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.Projects.AsNoTracking().Where(p => p.Id == projectId).Select(p => p.ProjectKey).FirstOrDefaultAsync(cancellationToken);
    }
}

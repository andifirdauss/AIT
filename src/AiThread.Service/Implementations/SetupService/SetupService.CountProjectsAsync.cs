using Microsoft.EntityFrameworkCore;

namespace AiThread.Service.Implementations;

public sealed partial class SetupService
{
    private Task<int> CountProjectsAsync(CancellationToken cancellationToken) =>
        Safe(async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            return await db.Projects.CountAsync(cancellationToken);
        }, 0);
}

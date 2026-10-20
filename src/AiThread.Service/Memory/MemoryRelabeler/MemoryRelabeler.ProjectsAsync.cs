using Microsoft.EntityFrameworkCore;

namespace AiThread.Service.Memory;

public sealed partial class MemoryRelabeler
{
    private async Task<IReadOnlyDictionary<Guid, (string? Key, string Name)>> ProjectsAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return (await db.Projects.AsNoTracking().Select(p => new { p.Id, p.ProjectKey, p.Name }).ToListAsync(cancellationToken))
            .ToDictionary(p => p.Id, p => ((string?)p.ProjectKey, p.Name));
    }
}

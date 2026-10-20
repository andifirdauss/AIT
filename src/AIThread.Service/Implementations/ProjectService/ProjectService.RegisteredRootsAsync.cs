using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Implementations;

public sealed partial class ProjectService
{
    private async Task<Dictionary<string, Guid>> RegisteredRootsAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var rows = await db.Projects.AsNoTracking().Select(p => new { p.RootPath, p.Id }).ToListAsync(cancellationToken);
        return rows.ToDictionary(r => r.RootPath, r => r.Id, StringComparer.OrdinalIgnoreCase);
    }
}

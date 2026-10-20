using AIThread.Common.DTOs;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Implementations;

public sealed partial class SkillService
{
    public async Task<IReadOnlyList<SkillSourceDto>> GetSourcesAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var sources = await db.SkillSources.AsNoTracking().OrderBy(s => s.CreatedAt).ToListAsync(cancellationToken);
        return sources.Select(ToDto).ToList();
    }
}

using AiThread.Common.DTOs;
using Microsoft.EntityFrameworkCore;

namespace AiThread.Service.Implementations;

public sealed partial class ProjectService
{
    public async Task<IReadOnlyList<ProjectDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var rows = await db.Projects.AsNoTracking()
            .OrderByDescending(p => p.LastSeenAt)
            .Select(p => new
            {
                Project = p,
                ThreadCount = p.Threads.Count(),
                SessionCount = p.Threads.Where(t => t.SessionId != null).Select(t => t.SessionId).Distinct().Count()
            })
            .ToListAsync(cancellationToken);

        var counts = await MemoryCountsAsync(rows.Select(r => r.Project).ToList(), null, cancellationToken);
        return rows.Select(r => ToDto(r.Project, r.ThreadCount, r.SessionCount, counts.GetValueOrDefault(r.Project.Id))).ToList();
    }
}

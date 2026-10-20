using AIThread.Common.DTOs;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Implementations;

public sealed partial class ProjectService
{
    public async Task<ProjectDetailDto?> GetDetailAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var project = await db.Projects.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (project is null) return null;

        var sessions = await db.Threads.AsNoTracking()
            .Where(t => t.ProjectId == id && t.SessionId != null)
            .GroupBy(t => t.SessionId!)
            .Select(g => new ProjectSessionDto(g.Key, g.Count(), g.Min(t => t.CreatedAt), g.Max(t => t.CreatedAt)))
            .ToListAsync(cancellationToken);

        var threadCount = await db.Threads.CountAsync(t => t.ProjectId == id, cancellationToken);
        var counts = await MemoryCountsAsync([project], project.ProjectKey, cancellationToken);

        var dto = ToDto(project, threadCount, sessions.Count, counts.GetValueOrDefault(id));
        return new ProjectDetailDto(dto, ProfileOf(project.RootPath).Modules, sessions.OrderByDescending(s => s.LastSeenAt).ToList());
    }
}

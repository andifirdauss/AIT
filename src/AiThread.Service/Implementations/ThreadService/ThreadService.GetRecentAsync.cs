using AiThread.Common.DTOs;
using Microsoft.EntityFrameworkCore;

namespace AiThread.Service.Implementations;

public sealed partial class ThreadService
{
    public async Task<IReadOnlyList<ThreadSummaryDto>> GetRecentAsync(int take, Guid? projectId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var query = db.Threads.AsNoTracking();
        if (projectId is { } id) query = query.Where(t => t.ProjectId == id);

        return await query
            .OrderByDescending(t => t.CreatedAt)
            .Take(Math.Clamp(take, 1, 200))
            .Select(SummaryProjection)
            .ToListAsync(cancellationToken);
    }
}

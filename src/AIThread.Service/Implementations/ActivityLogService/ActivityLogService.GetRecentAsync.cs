using AIThread.Common.DTOs;
using AIThread.Common.Enums;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Implementations;

public sealed partial class ActivityLogService
{
    public async Task<IReadOnlyList<ActivityLogDto>> GetRecentAsync(int take, ActivityLevel? minLevel, Guid? threadId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var query = db.ActivityLogs.AsNoTracking();
        if (threadId is { } id) query = query.Where(l => l.ThreadId == id);
        if (minLevel is { } level)
        {
            // Levels are stored as strings, so compare by membership instead of by order.
            var allowed = Enum.GetValues<ActivityLevel>().Where(v => v >= level).ToList();
            query = query.Where(l => allowed.Contains(l.Level));
        }

        var rows = await query.OrderByDescending(l => l.Id).Take(Math.Clamp(take, 1, 1000)).ToListAsync(cancellationToken);
        return rows.Select(ToDto).ToList();
    }
}

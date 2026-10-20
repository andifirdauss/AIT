using AiThread.Common.Enums;
using Microsoft.EntityFrameworkCore;

namespace AiThread.Service.Business;

public sealed partial class BusinessDigestCollector
{
    // Project keys of this machine that have threads waiting.
    public async Task<IReadOnlyList<(string Key, string Name)>> PendingProjectsAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var rows = await db.Threads.AsNoTracking()
            .Where(t => t.Status == ThreadStatus.Completed && t.BusinessDigestedAt == null && t.Project != null && t.Project.ProjectKey != "")
            .Select(t => new { t.Project!.ProjectKey, t.Project.Name })
            .Distinct()
            .ToListAsync(cancellationToken);
        return rows.GroupBy(r => r.ProjectKey).Select(g => (g.Key, g.First().Name)).ToList();
    }
}

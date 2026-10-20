using AiThread.Common.DTOs;
using AiThread.Common.Entities;
using AiThread.Common.Enums;
using AiThread.Service.Workspace;
using Microsoft.EntityFrameworkCore;

namespace AiThread.Service.Implementations;

public sealed partial class BusinessMemoryService
{
    public async Task<BusinessOverviewDto?> GetOverviewAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        var project = await ProjectAsync(projectId, cancellationToken);
        if (project is null) return null;

        var key = project.ProjectKey;
        List<BusinessPage> rows;
        await using (var db = await store.CreateAsync(cancellationToken))
            rows = await SummaryQuery(db, key).ToListAsync(cancellationToken);

        // Threads of every clone of the project registered on this machine.
        int pending;
        DateTime? lastDigest;
        await using (var db = await dbFactory.CreateDbContextAsync(cancellationToken))
        {
            var threads = db.Threads.AsNoTracking().Where(t => t.Project != null && t.Project.ProjectKey == key);
            pending = await threads.CountAsync(t => t.Status == ThreadStatus.Completed && t.BusinessDigestedAt == null, cancellationToken);
            lastDigest = await threads.MaxAsync(t => t.BusinessDigestedAt, cancellationToken);
        }

        return new BusinessOverviewDto(
            project.Id,
            key,
            project.Name,
            !ProjectKeyResolver.IsLocal(key),
            rows.OrderBy(p => p.Module).ThenBy(p => p.Title).Select(ToSummary).ToList(),
            pending,
            lastDigest,
            runner.IsRunning(key),
            store.Description);
    }
}

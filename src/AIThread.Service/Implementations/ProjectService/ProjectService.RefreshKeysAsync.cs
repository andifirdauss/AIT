using AIThread.Common.DTOs;
using AIThread.Common.Entities;
using AIThread.Service.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Implementations;

public sealed partial class ProjectService
{
    public async Task RefreshKeysAsync(CancellationToken cancellationToken = default)
    {
        List<Project> projects;
        var changed = new List<(Project Project, string Previous)>();
        await using (var db = await dbFactory.CreateDbContextAsync(cancellationToken))
        {
            projects = await db.Projects.ToListAsync(cancellationToken);
            foreach (var project in projects)
            {
                var previous = ApplyKey(project);
                if (previous != project.ProjectKey) changed.Add((project, previous));
            }
            if (changed.Count > 0) await db.SaveChangesAsync(cancellationToken);
        }

        foreach (var (project, previous) in changed) await OnKeyChangedAsync(project, previous, cancellationToken);
        var filled = await memoryKeys.BackfillAsync(projects, cancellationToken);
        if (filled > 0)
        {
            activityLog.Info("memory", $"{filled} memory lama diberi project key agar bisa dibagi antar developer.");
            eventBus.Publish(AppEvent.Create(AppEventTypes.MemoryChanged, new { id = (Guid?)null, action = "updated" }));
        }
    }
}

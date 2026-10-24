using AIThread.Common.Enums;
using AIThread.Service.Models;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Orchestration;

public sealed partial class ThreadOrchestrator
{
    // The chat's last thread when it belongs to this project, has finished, and still has planned tasks that did not
    // succeed; null otherwise, and the reply then reaches the planner as a normal prompt.
    private async Task<Guid?> RetryableSessionThreadAsync(ThreadRequest request, CancellationToken cancellationToken)
    {
        var threadId = request.Session?.ThreadIds.LastOrDefault() ?? Guid.Empty;
        if (threadId == Guid.Empty) return null;

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var thread = await db.Threads.AsNoTracking()
            .Where(t => t.Id == threadId && t.ProjectId == request.ProjectId)
            .Select(t => new { t.Status, t.PlanJson })
            .FirstOrDefaultAsync(cancellationToken);
        if (thread is null || thread.Status is not (ThreadStatus.Completed or ThreadStatus.Failed or ThreadStatus.Cancelled)) return null;
        if (string.IsNullOrWhiteSpace(thread.PlanJson) || !ThreadPlan.TryParse(thread.PlanJson, out var plan, out _) || plan is null) return null;

        var succeeded = await db.ThreadTasks.AsNoTracking()
            .Where(t => t.ThreadRunId == threadId && t.Status == ThreadTaskStatus.Succeeded)
            .Select(t => t.TaskKey)
            .ToListAsync(cancellationToken);
        return plan.Tasks.Any(t => !succeeded.Contains(t.Id, StringComparer.OrdinalIgnoreCase)) ? threadId : null;
    }
}

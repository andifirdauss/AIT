using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Orchestration;

public sealed partial class ThreadOrchestrator
{
    // The tasks an earlier run of this thread executed (committed with commit_tasks before the planner paused), as the
    // results later tasks may depend on and the final report counts.
    private async Task<IReadOnlyList<TaskResult>> EarlierResultsAsync(Guid threadId, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var tasks = await db.ThreadTasks.AsNoTracking()
            .Where(t => t.ThreadRunId == threadId)
            .OrderBy(t => t.Order)
            .ToListAsync(cancellationToken);
        return tasks.Select(t => new TaskResult(t.TaskKey, t.FilePath, t.Status, t.Summary, t.Diff, t.Error, t.DurationMs ?? 0, t.Tokens)).ToList();
    }
}

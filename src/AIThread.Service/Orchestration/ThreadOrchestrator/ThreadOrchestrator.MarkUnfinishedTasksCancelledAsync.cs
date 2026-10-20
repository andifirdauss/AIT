using AIThread.Common.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.Orchestration;

public sealed partial class ThreadOrchestrator
{
    private async Task MarkUnfinishedTasksCancelledAsync(Guid threadId)
    {
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync();
            await db.ThreadTasks
                .Where(t => t.ThreadRunId == threadId && (t.Status == ThreadTaskStatus.Pending || t.Status == ThreadTaskStatus.Running))
                .ExecuteUpdateAsync(s => s
                    .SetProperty(t => t.Status, ThreadTaskStatus.Cancelled)
                    .SetProperty(t => t.CompletedAt, DateTime.UtcNow));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not mark tasks of thread {ThreadId} as cancelled", threadId);
        }
    }
}

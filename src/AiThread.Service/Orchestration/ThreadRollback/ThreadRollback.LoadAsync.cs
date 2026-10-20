using Microsoft.EntityFrameworkCore;

namespace AiThread.Service.Orchestration;

public sealed partial class ThreadRollback
{
    private async Task<RollbackState?> LoadAsync(Guid threadId, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var thread = await db.Threads.AsNoTracking().Include(t => t.Project).FirstOrDefaultAsync(t => t.Id == threadId, cancellationToken);
        if (thread is null) return null;

        var snapshots = await db.ThreadFileSnapshots.AsNoTracking()
            .Where(s => s.ThreadRunId == threadId)
            .OrderBy(s => s.FilePath)
            .ToListAsync(cancellationToken);
        return new RollbackState(thread, thread.Project?.RootPath, snapshots, registry.IsRunning(threadId));
    }
}

using AIThread.Common.Entities;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Implementations;

public sealed partial class MemoryService
{
    // Git of the thread a memory came from (branch, commit and author when it started), from this machine's threads;
    // null without a thread or when it is gone.
    private async Task<ThreadRun?> GitOriginAsync(Guid? threadId, CancellationToken cancellationToken)
    {
        if (threadId is not { } id) return null;
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.Threads.AsNoTracking()
            .Where(t => t.Id == id)
            .Select(t => new ThreadRun { GitAuthor = t.GitAuthor, GitBranch = t.GitBranch, GitBaseCommit = t.GitBaseCommit })
            .FirstOrDefaultAsync(cancellationToken);
    }
}

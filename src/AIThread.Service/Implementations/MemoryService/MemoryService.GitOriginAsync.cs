using AIThread.Common.Entities;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Implementations;

public sealed partial class MemoryService
{
    // Git of the thread a memory came from (branch, commit and author when it started), from this machine's threads; for
    // a commit made outside AIT (no thread here), its shared ThreadChange: the commit's author, ref and the commit itself.
    // Null without a source or when it is gone.
    private async Task<ThreadRun?> GitOriginAsync(Guid? threadId, CancellationToken cancellationToken)
    {
        if (threadId is not { } id) return null;
        await using (var db = await dbFactory.CreateDbContextAsync(cancellationToken))
        {
            var thread = await db.Threads.AsNoTracking()
                .Where(t => t.Id == id)
                .Select(t => new ThreadRun { GitAuthor = t.GitAuthor, GitBranch = t.GitBranch, GitBaseCommit = t.GitBaseCommit })
                .FirstOrDefaultAsync(cancellationToken);
            if (thread is not null) return thread;
        }
        try
        {
            await using var git = await longStore.CreateGitAsync(cancellationToken);
            return await git.ThreadChanges.AsNoTracking()
                .Where(c => c.Id == id)
                .Select(c => new ThreadRun { GitAuthor = c.GitAuthor, GitBranch = c.GitBranch, GitBaseCommit = c.GitBaseCommit })
                .FirstOrDefaultAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LongStoreFailed(ex, "Could not read the git origin of a memory");
            return null;
        }
    }
}

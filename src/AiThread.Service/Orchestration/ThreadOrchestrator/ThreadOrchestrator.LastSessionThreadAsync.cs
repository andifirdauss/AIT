using AiThread.Common.Enums;
using AiThread.Service.Models;
using Microsoft.EntityFrameworkCore;

namespace AiThread.Service.Orchestration;

public sealed partial class ThreadOrchestrator
{
    // The latest earlier AIT thread of this chat (not the running one), with the files its succeeded tasks left behind.
    private async Task<SessionThread?> LastSessionThreadAsync(SessionContext? session, Guid currentThreadId, CancellationToken cancellationToken)
    {
        var threadId = session?.ThreadIds.LastOrDefault(id => id != currentThreadId) ?? Guid.Empty;
        if (threadId == Guid.Empty) return null;

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var prompt = await db.Threads.AsNoTracking().Where(t => t.Id == threadId).Select(t => t.Prompt).FirstOrDefaultAsync(cancellationToken);
        if (prompt is null) return null;

        var tasks = await db.ThreadTasks.AsNoTracking()
            .Where(t => t.ThreadRunId == threadId && t.Status == ThreadTaskStatus.Succeeded)
            .OrderBy(t => t.Order)
            .Select(t => new { t.FilePath, t.TargetPath })
            .ToListAsync(cancellationToken);
        var files = tasks.Select(t => string.IsNullOrWhiteSpace(t.TargetPath) ? t.FilePath : t.TargetPath)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return new SessionThread(threadId, prompt, files);
    }
}

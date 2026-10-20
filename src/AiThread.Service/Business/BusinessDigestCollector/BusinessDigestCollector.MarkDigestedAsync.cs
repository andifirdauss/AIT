using Microsoft.EntityFrameworkCore;

namespace AiThread.Service.Business;

public sealed partial class BusinessDigestCollector
{
    public async Task MarkDigestedAsync(IReadOnlyCollection<Guid> threadIds, CancellationToken cancellationToken)
    {
        if (threadIds.Count == 0) return;
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var now = DateTime.UtcNow;
        await db.Threads.Where(t => threadIds.Contains(t.Id)).ExecuteUpdateAsync(s => s.SetProperty(t => t.BusinessDigestedAt, now), cancellationToken);
        // Screenshots that mattered are on a page now; the rest is not needed any more.
        await db.ThreadAttachments.Where(a => threadIds.Contains(a.ThreadRunId)).ExecuteDeleteAsync(cancellationToken);
    }
}

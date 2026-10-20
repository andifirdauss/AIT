using Microsoft.EntityFrameworkCore;

namespace AiThread.Service.Business;

public sealed partial class BusinessDigestCollector
{
    // Images of threads that never reach the wiki (failed threads, business memory turned off) are not kept forever.
    public async Task<int> PruneAttachmentsAsync(TimeSpan maxAge, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var cutoff = DateTime.UtcNow - maxAge;
        return await db.ThreadAttachments.Where(a => a.CreatedAt < cutoff).ExecuteDeleteAsync(cancellationToken);
    }
}

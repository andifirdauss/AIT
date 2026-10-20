using Microsoft.EntityFrameworkCore;

namespace AiThread.Service.Business;

public sealed partial class BusinessDigestCollector
{
    public async Task<int> CountPendingAsync(string projectKey, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await Pending(db, projectKey).CountAsync(cancellationToken);
    }
}

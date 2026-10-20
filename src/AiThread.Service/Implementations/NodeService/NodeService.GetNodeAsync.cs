using AiThread.Common.Entities;
using Microsoft.EntityFrameworkCore;

namespace AiThread.Service.Implementations;

public sealed partial class NodeService
{
    public async Task<AiNode?> GetNodeAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await WithDetails(db.Nodes.AsNoTracking()).FirstOrDefaultAsync(n => n.Id == id, cancellationToken);
    }
}

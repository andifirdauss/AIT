using AIThread.Common.Entities;
using AIThread.Common.Enums;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Implementations;

public sealed partial class NodeService
{
    public async Task<IReadOnlyList<AiNode>> GetActiveAsync(NodeRole role, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await WithDetails(db.Nodes.AsNoTracking())
            .Where(n => n.Role == role && n.IsActive)
            .OrderBy(n => n.Priority).ThenBy(n => n.Name)
            .ToListAsync(cancellationToken);
    }
}

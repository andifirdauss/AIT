using AiThread.Common.DTOs;
using Microsoft.EntityFrameworkCore;

namespace AiThread.Service.Implementations;

public sealed partial class NodeService
{
    public async Task<NodeDto?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var node = await WithDetails(db.Nodes.AsNoTracking()).FirstOrDefaultAsync(n => n.Id == id, cancellationToken);
        return node is null ? null : ToDto(node, await FallbackNamesAsync(db, node, cancellationToken));
    }
}

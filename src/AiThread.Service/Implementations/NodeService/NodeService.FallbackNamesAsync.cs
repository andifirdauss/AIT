using AiThread.Common.Entities;
using AiThread.Data.Context;
using Microsoft.EntityFrameworkCore;

namespace AiThread.Service.Implementations;

public sealed partial class NodeService
{
    private static async Task<Dictionary<Guid, string>> FallbackNamesAsync(AiThreadDbContext db, AiNode node, CancellationToken cancellationToken)
    {
        if (node.FallbackNodeId is not { } id) return [];
        var name = await db.Nodes.AsNoTracking().Where(n => n.Id == id).Select(n => n.Name).FirstOrDefaultAsync(cancellationToken);
        return name is null ? [] : new Dictionary<Guid, string> { [id] = name };
    }
}

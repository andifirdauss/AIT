using AIThread.Data.Context;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Implementations;

public sealed partial class NodeService
{
    // The foreign key to the tools node does not cascade (SQL Server allows one cascade path to Nodes).
    private static async Task<int> ReleaseDelegationsAsync(AIThreadDbContext db, Guid toolsNodeId, CancellationToken cancellationToken)
    {
        var delegations = await db.PlannerToolDelegations.Where(d => d.ToolsNodeId == toolsNodeId).ToListAsync(cancellationToken);
        db.PlannerToolDelegations.RemoveRange(delegations);
        return delegations.Count;
    }
}

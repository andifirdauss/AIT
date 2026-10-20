using AiThread.Common.DTOs;
using AiThread.Service.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace AiThread.Service.Implementations;

public sealed partial class NodeService
{
    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var node = await db.Nodes.FirstOrDefaultAsync(n => n.Id == id, cancellationToken);
        if (node is null) return false;

        // The foreign key does not null references itself (SQL Server forbids it on a self reference).
        var dependents = await db.Nodes.Where(n => n.FallbackNodeId == id).ToListAsync(cancellationToken);
        foreach (var dependent in dependents) dependent.FallbackNodeId = null;
        var toolsReleased = await ReleaseDelegationsAsync(db, id, cancellationToken);
        db.Nodes.Remove(node);
        await db.SaveChangesAsync(cancellationToken);

        eventBus.Publish(AppEvent.Create(AppEventTypes.NodeChanged, new { node.Id, action = "deleted" }));
        activityLog.Info("node", $"Node {node.Role} '{node.Name}' dihapus." +
                                 (toolsReleased > 0 ? $" {toolsReleased} delegasi tool planner dilepas dan kembali dijalankan planner." : string.Empty));
        return true;
    }
}

using AIThread.Common.DTOs;
using AIThread.Common.Localization;
using AIThread.Service.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Implementations;

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
        activityLog.Info("node", Lang.T($"Node {node.Role} '{node.Name}' dihapus.", $"{node.Role} node '{node.Name}' deleted.") +
                                 (toolsReleased > 0 ? Lang.T($" {toolsReleased} delegasi tool planner dilepas dan kembali dijalankan planner.", $" {toolsReleased} planner tool delegations released; the planner runs them again.") : string.Empty));
        return true;
    }
}

using AiThread.Common.DTOs;
using AiThread.Common.Enums;
using AiThread.Service.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace AiThread.Service.Implementations;

public sealed partial class NodeService
{
    public async Task<NodeDto?> UpdateAsync(Guid id, SaveNodeRequest request, CancellationToken cancellationToken = default)
    {
        Validate(request);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var node = await WithDetails(db.Nodes).FirstOrDefaultAsync(n => n.Id == id, cancellationToken);
        if (node is null) return null;

        var previousRole = node.Role;
        Apply(node, request);
        await ValidateFallbackAsync(db, node, cancellationToken);
        await ApplySkillsAsync(db, node, request.SkillIds, cancellationToken);
        await ApplyDelegationsAsync(db, node, request.ToolDelegations, cancellationToken);
        node.UpdatedAt = DateTime.UtcNow;

        // A node that changes role can no longer back up nodes of its old role, nor run tools for planners.
        var released = 0;
        var toolsReleased = 0;
        if (previousRole != node.Role)
        {
            var dependents = await db.Nodes.Where(n => n.FallbackNodeId == node.Id).ToListAsync(cancellationToken);
            foreach (var dependent in dependents) dependent.FallbackNodeId = null;
            released = dependents.Count;
            if (previousRole == NodeRole.ToolsNode) toolsReleased = await ReleaseDelegationsAsync(db, node.Id, cancellationToken);
        }
        await db.SaveChangesAsync(cancellationToken);

        eventBus.Publish(AppEvent.Create(AppEventTypes.NodeChanged, new { node.Id, action = "updated" }));
        var names = await FallbackNamesAsync(db, node, cancellationToken);
        activityLog.Info("node", $"Node {node.Role} '{node.Name}' diperbarui ({node.Model}){SkillNote(node)}{DelegationNote(node)}{FallbackNote(node, names)}." +
                                 (released > 0 ? $" {released} node lain tidak lagi memakainya sebagai cadangan karena role berubah." : string.Empty) +
                                 (toolsReleased > 0 ? $" {toolsReleased} delegasi tool planner dilepas dan kembali dijalankan planner." : string.Empty));
        return ToDto(node, names);
    }
}

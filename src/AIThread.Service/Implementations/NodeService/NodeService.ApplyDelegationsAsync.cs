using AIThread.Common.DTOs;
using AIThread.Common.Entities;
using AIThread.Common.Enums;
using AIThread.Data.Context;
using AIThread.Service.Models;
using AIThread.Service.Orchestration;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Implementations;

public sealed partial class NodeService
{
    // Only planners delegate; another role drops its delegations. null = leave them as they are.
    private static async Task ApplyDelegationsAsync(AIThreadDbContext db, AiNode node, IReadOnlyList<ToolDelegationRequest>? delegations, CancellationToken cancellationToken)
    {
        if (node.Role != NodeRole.Planner)
        {
            node.ToolDelegations.Clear();
            return;
        }
        if (delegations is null) return;

        var wanted = new Dictionary<string, Guid>(StringComparer.Ordinal);
        foreach (var delegation in delegations)
        {
            var tool = delegation.Tool?.Trim() ?? string.Empty;
            if (!PlannerTools.Delegable.Contains(tool))
                throw new AppValidationException($"Tool '{tool}' tidak bisa didelegasikan. Pilihan: {string.Join(", ", PlannerTools.Delegable)}.");
            if (!wanted.TryAdd(tool, delegation.ToolsNodeId))
                throw new AppValidationException($"Tool '{tool}' didelegasikan lebih dari sekali; satu tool hanya untuk satu tools node.");
        }

        var toolsNodeIds = wanted.Values.Distinct().ToList();
        var toolsNodes = await db.Nodes.AsNoTracking().Where(n => toolsNodeIds.Contains(n.Id))
            .Select(n => new { n.Id, n.Name, n.Role }).ToDictionaryAsync(n => n.Id, cancellationToken);
        foreach (var toolsNodeId in toolsNodeIds)
        {
            if (!toolsNodes.TryGetValue(toolsNodeId, out var toolsNode)) throw new AppValidationException("Tools node untuk delegasi tool tidak ditemukan.");
            if (toolsNode.Role != NodeRole.ToolsNode)
                throw new AppValidationException($"Tool hanya bisa didelegasikan ke node ber-role ToolsNode, sedangkan '{toolsNode.Name}' ber-role {toolsNode.Role}.");
        }

        node.ToolDelegations.RemoveAll(d => !wanted.TryGetValue(d.Tool, out var toolsNodeId) || toolsNodeId != d.ToolsNodeId);
        foreach (var (tool, toolsNodeId) in wanted)
        {
            if (node.ToolDelegations.Any(d => d.Tool == tool)) continue;
            node.ToolDelegations.Add(new PlannerToolDelegation { PlannerNodeId = node.Id, Tool = tool, ToolsNodeId = toolsNodeId });
        }
    }
}

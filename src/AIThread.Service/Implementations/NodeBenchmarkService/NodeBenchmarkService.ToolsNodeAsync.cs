using AIThread.Common.Entities;
using AIThread.Common.Enums;

namespace AIThread.Service.Implementations;

public sealed partial class NodeBenchmarkService
{
    // The tools node a small change goes through: none when the planner reads for itself.
    private async Task<AiNode?> ToolsNodeAsync(AiNode? planner, CancellationToken cancellationToken)
    {
        if (planner is null or { DirectRead: true, DirectReadPercent: > 0 }) return null;
        foreach (var id in planner.ToolDelegations.Select(d => d.ToolsNodeId).Distinct())
            if (await nodes.GetNodeAsync(id, cancellationToken) is { IsActive: true, Role: NodeRole.ToolsNode } node)
                return node;
        return null;
    }
}

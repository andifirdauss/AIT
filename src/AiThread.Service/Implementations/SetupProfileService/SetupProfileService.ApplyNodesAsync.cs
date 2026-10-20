using AiThread.Common.DTOs;
using AiThread.Common.Enums;

namespace AiThread.Service.Implementations;

public sealed partial class SetupProfileService
{
    // Pass 1 creates the new nodes without links, so pass 2 can point fallbacks and delegations at any node of the
    // file. Others (Replace mode) are deactivated, never deleted.
    private async Task ApplyNodesAsync(IReadOnlyList<ResolvedNode> resolved, IReadOnlyList<NodeDto> others, CancellationToken cancellationToken)
    {
        foreach (var r in resolved.Where(r => r.Existing is null))
            await nodeService.CreateAsync(ToRequest(r.Node, null, null, null), cancellationToken);

        var all = await nodeService.GetAllAsync(cancellationToken);
        Guid? IdOf(NodeRole role, string? name) => name is null ? null : all.FirstOrDefault(n => IsNode(n.Role, n.Name, role, name))?.Id;

        foreach (var r in resolved.Where(r => r.Changed))
        {
            if (IdOf(r.Node.Role, r.Node.Name) is not { } id) continue;
            var delegations = r.Node.Role == NodeRole.Planner
                ? r.Delegations
                    .Select(d => IdOf(NodeRole.ToolsNode, d.ToolsNode) is { } toolsNodeId ? new ToolDelegationRequest(d.Tool, toolsNodeId) : null)
                    .OfType<ToolDelegationRequest>()
                    .ToList()
                : null;
            await nodeService.UpdateAsync(id, ToRequest(r.Node, IdOf(r.Node.Role, r.Fallback), r.SkillIds, delegations), cancellationToken);
        }

        foreach (var other in others)
        {
            await nodeService.UpdateAsync(other.Id, new SaveNodeRequest(other.Name, other.Role, other.Model, other.Temperature, other.MaxTokens,
                other.MaxConcurrency, other.Priority, other.SystemPrompt, false, null, other.FallbackNodeId, null), cancellationToken);
        }
    }
}

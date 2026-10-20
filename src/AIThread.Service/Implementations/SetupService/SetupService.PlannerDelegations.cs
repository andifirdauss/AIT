using AIThread.Common.DTOs;

namespace AIThread.Service.Implementations;

public sealed partial class SetupService
{
    // Delegations to other tools nodes are kept. The preset tools node gets the heavy tools the planner does not hand
    // elsewhere yet, or loses its delegations when it is unticked (activeToolsNodeId null).
    private static IReadOnlyList<ToolDelegationRequest> PlannerDelegations(NodeDto? planner, Guid? presetToolsNodeId, Guid? activeToolsNodeId)
    {
        var kept = (planner?.ToolDelegations ?? [])
            .Where(d => d.ToolsNodeId != presetToolsNodeId)
            .Select(d => new ToolDelegationRequest(d.Tool, d.ToolsNodeId))
            .ToList();
        if (activeToolsNodeId is { } toolsNodeId)
            kept.AddRange(DelegatedTools.Where(tool => kept.All(d => d.Tool != tool)).Select(tool => new ToolDelegationRequest(tool, toolsNodeId)));
        return kept;
    }
}

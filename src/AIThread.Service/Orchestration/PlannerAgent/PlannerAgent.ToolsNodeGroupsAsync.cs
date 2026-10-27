using AIThread.Common.Entities;
using AIThread.Common.Enums;

namespace AIThread.Service.Orchestration;

public sealed partial class PlannerAgent
{
    // Active tools nodes only (an inactive one gives its tools back to the planner), loaded with their skills. run_build
    // stays out when AIT cannot build the project, the git tools outside a git repository and run_command when it is
    // turned off, as they do for the planner.
    private async Task<IReadOnlyList<ToolsNodeGroup>> ToolsNodeGroupsAsync(AiNode planner, bool canBuild, bool canGit, bool canCommand, bool canFetch, CancellationToken cancellationToken)
    {
        var groups = new List<ToolsNodeGroup>();
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var byToolsNode = planner.ToolDelegations
            .Where(d => PlannerTools.Delegable.Contains(d.Tool) && PlannerTools.Offered(d.Tool, canBuild, canGit, canCommand, canFetch))
            .GroupBy(d => d.ToolsNodeId)
            .OrderBy(g => g.Min(d => PlannerTools.Delegable.IndexOf(d.Tool)));
        foreach (var delegations in byToolsNode)
        {
            var toolsNode = await nodeService.GetNodeAsync(delegations.Key, cancellationToken);
            if (toolsNode is not { IsActive: true, Role: NodeRole.ToolsNode }) continue;
            var tools = delegations.Select(d => d.Tool).OrderBy(t => PlannerTools.Delegable.IndexOf(t)).ToList();
            groups.Add(new ToolsNodeGroup(UniqueKey(toolsNode.Name, keys), toolsNode, tools));
        }
        return groups;
    }
}

using AiThread.Common.Entities;
using Microsoft.EntityFrameworkCore;

namespace AiThread.Service.Implementations;

public sealed partial class NodeService
{
    // Skills with their source, in prompt order: the agents read their content, the UI their names. Tool delegations
    // with their tools node (without its skills: the planner loads a tools node through GetNodeAsync before calling it).
    private static IQueryable<AiNode> WithDetails(IQueryable<AiNode> nodes) =>
        nodes.Include(n => n.Skills.OrderBy(s => s.Order)).ThenInclude(s => s.Skill!).ThenInclude(s => s.SkillSource)
            .Include(n => n.ToolDelegations).ThenInclude(d => d.ToolsNode)
            .AsSplitQuery();
}

using AIThread.Common.Entities;

namespace AIThread.Service.Orchestration;

public sealed partial class PlannerAgent
{
    // Without a tools node the planner holds every tool anyway, and 0% leaves all reading to the tools nodes.
    private static bool CanReadDirectly(AiNode node, IReadOnlyList<ToolsNodeGroup> groups) =>
        node.DirectRead && node.DirectReadPercent > 0 && groups.Count > 0;
}

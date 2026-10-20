namespace AIThread.Common.Entities;

// A planner tool handed to a tools node: the planner no longer calls it itself, but asks the tools node through
// "investigate". One tools node per tool and planner; a planner may use several tools nodes.
public class PlannerToolDelegation
{
    public Guid PlannerNodeId { get; set; }
    public AiNode? PlannerNode { get; set; }

    // Planner tool name, e.g. "read_file".
    public string Tool { get; set; } = string.Empty;

    public Guid ToolsNodeId { get; set; }
    public AiNode? ToolsNode { get; set; }
}

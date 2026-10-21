using System.Text.Json.Nodes;
using AIThread.Common.Entities;
using AIThread.Service.Models;

namespace AIThread.Service.Orchestration;

public sealed partial class PlannerAgent
{
    // The planner can read a skill's supporting files with its read tools.
    private async Task<JsonObject> SystemMessageAsync(AiNode node, IReadOnlyList<ToolsNodeGroup> groups, bool autoTesting, CancellationToken cancellationToken) =>
        ChatMessages.System(await prompts.BuildAsync(
            AgentPrompts.Planner + (groups.Count == 0 ? string.Empty : AgentPrompts.PlannerDelegation(groups)) + (autoTesting ? AgentPrompts.PlannerTesting : string.Empty),
            node, cancellationToken, canReadFiles: true));
}

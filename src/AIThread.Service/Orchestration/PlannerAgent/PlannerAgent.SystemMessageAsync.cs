using System.Text.Json.Nodes;
using AIThread.Common.Entities;
using AIThread.Service.Models;

namespace AIThread.Service.Orchestration;

public sealed partial class PlannerAgent
{
    // The planner can read a skill's supporting files with its read tools.
    private async Task<JsonObject> SystemMessageAsync(AiNode node, IReadOnlyList<ToolsNodeGroup> groups, CancellationToken cancellationToken) =>
        ChatMessages.System(await prompts.BuildAsync(
            groups.Count == 0 ? AgentPrompts.Planner : AgentPrompts.Planner + AgentPrompts.PlannerDelegation(groups), node, cancellationToken, canReadFiles: true));
}

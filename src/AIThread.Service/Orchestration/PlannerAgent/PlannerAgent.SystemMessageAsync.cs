using System.Text.Json.Nodes;
using AIThread.Common.Entities;
using AIThread.Common.Enums;
using AIThread.Service.Models;

namespace AIThread.Service.Orchestration;

public sealed partial class PlannerAgent
{
    // The planner can read a skill's supporting files with its read tools. unitTests: how the project adds new tests,
    // null when it has no unit test catalog.
    private async Task<JsonObject> SystemMessageAsync(AiNode node, IReadOnlyList<ToolsNodeGroup> groups, bool autoTesting, UnitTestAuthoring? unitTests,
        CancellationToken cancellationToken) =>
        ChatMessages.System(await prompts.BuildAsync(
            AgentPrompts.Planner + (groups.Count == 0 ? string.Empty : AgentPrompts.PlannerDelegation(groups)) + (autoTesting ? AgentPrompts.PlannerTesting : string.Empty)
            + (unitTests is { } authoring ? AgentPrompts.PlannerUnitTests(authoring, autoTesting) : string.Empty),
            node, cancellationToken, canReadFiles: true));
}

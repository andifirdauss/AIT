using System.Text.Json.Nodes;
using AIThread.Common.Entities;
using AIThread.Common.Enums;
using AIThread.Service.Memory;
using AIThread.Service.Models;

namespace AIThread.Service.Orchestration;

public sealed partial class PlannerAgent
{
    // The planner can read a skill's supporting files with its read tools. unitTests: how the project adds new tests,
    // null when it has no unit test catalog. stream: commit_tasks is offered (PlanStream).
    private async Task<JsonObject> SystemMessageAsync(AiNode node, IReadOnlyList<ToolsNodeGroup> groups, bool directRead, bool autoTesting, UnitTestAuthoring? unitTests,
        PlanStream? stream, CancellationToken cancellationToken) =>
        ChatMessages.System(await prompts.BuildAsync(
            AgentPrompts.Planner + (groups.Count == 0 ? string.Empty : AgentPrompts.PlannerDelegation(groups, directRead)) + (autoTesting ? AgentPrompts.PlannerTesting : string.Empty)
            + (unitTests is { } authoring ? AgentPrompts.PlannerUnitTests(authoring, autoTesting) : string.Empty)
            + (stream is null ? string.Empty : AgentPrompts.PlannerBatches(stream.ExecutesEarly))
            + "\n\n# UI language\n" + TextLanguage.Ui,
            node, cancellationToken, canReadFiles: true));
}

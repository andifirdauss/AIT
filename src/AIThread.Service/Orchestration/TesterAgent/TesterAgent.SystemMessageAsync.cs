using System.Text.Json.Nodes;
using AIThread.Common.Entities;
using AIThread.Service.Models;

namespace AIThread.Service.Orchestration;

public sealed partial class TesterAgent
{
    // The node's additional instructions are where a team keeps its test login.
    private async Task<JsonObject> SystemMessageAsync(AiNode node, CancellationToken cancellationToken) =>
        ChatMessages.System(await prompts.BuildAsync(AgentPrompts.Tester, node, cancellationToken));
}

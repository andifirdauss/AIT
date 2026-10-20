using System.Text.Json.Nodes;
using AIThread.Common.Entities;
using AIThread.Service.Models;

namespace AIThread.Service.Orchestration;

public sealed partial class ToolsNodeAgent
{
    private async Task<JsonObject> SystemMessageAsync(AiNode node, CancellationToken cancellationToken) =>
        ChatMessages.System(await prompts.BuildAsync(AgentPrompts.ToolsNode, node, cancellationToken, canReadFiles: true));
}

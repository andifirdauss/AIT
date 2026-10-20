using System.Text.Json.Nodes;
using AiThread.Common.Entities;
using AiThread.Service.Models;

namespace AiThread.Service.Orchestration;

public sealed partial class ToolsNodeAgent
{
    private async Task<JsonObject> SystemMessageAsync(AiNode node, CancellationToken cancellationToken) =>
        ChatMessages.System(await prompts.BuildAsync(AgentPrompts.ToolsNode, node, cancellationToken, canReadFiles: true));
}

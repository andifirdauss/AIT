using AiThread.Common.Entities;

namespace AiThread.Service.Implementations;

public sealed partial class NodeService
{
    private static string DelegationNote(AiNode node) =>
        node.ToolDelegations.Count == 0 ? string.Empty : $", {node.ToolDelegations.Count} tool ke tools node";
}

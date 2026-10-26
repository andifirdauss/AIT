using AIThread.Common.Entities;
using AIThread.Common.Localization;

namespace AIThread.Service.Implementations;

public sealed partial class NodeService
{
    private static string DelegationNote(AiNode node) =>
        (node.ToolDelegations.Count == 0 ? string.Empty : Lang.T($", {node.ToolDelegations.Count} tool ke tools node", $", {node.ToolDelegations.Count} tools to a tools node")) +
        (node.AutoTesting ? ", Auto Testing" : string.Empty);
}

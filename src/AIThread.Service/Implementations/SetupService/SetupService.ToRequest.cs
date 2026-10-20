using AIThread.Common.DTOs;

namespace AIThread.Service.Implementations;

public sealed partial class SetupService
{
    // The node as it is; skills and delegations null = kept by NodeService.
    private static SaveNodeRequest ToRequest(NodeDto node) =>
        new(node.Name, node.Role, node.Model, node.Temperature, node.MaxTokens, node.MaxConcurrency, node.Priority, node.SystemPrompt,
            node.IsActive, null, node.FallbackNodeId, null);
}

using AIThread.Common.DTOs;

namespace AIThread.Service.Implementations;

public sealed partial class SetupProfileService
{
    // A profile written before Auto Testing existed leaves it as it is on this machine.
    private static SaveNodeRequest ToRequest(SetupProfileNode node, Guid? fallbackNodeId, IReadOnlyList<Guid>? skillIds,
        IReadOnlyList<ToolDelegationRequest>? delegations) =>
        new(node.Name, node.Role, node.Model, node.Temperature, node.MaxTokens, node.MaxConcurrency, node.Priority, Clean(node.SystemPrompt),
            node.IsActive, skillIds, fallbackNodeId, delegations, node.AutoTesting);
}

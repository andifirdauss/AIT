using AIThread.Common.DTOs;

namespace AIThread.Service.Implementations;

public sealed partial class SetupProfileService
{
    // A profile written before Auto Testing or direct reading existed leaves them as they are on this machine.
    private static SaveNodeRequest ToRequest(SetupProfileNode node, Guid? fallbackNodeId, IReadOnlyList<Guid>? skillIds,
        IReadOnlyList<ToolDelegationRequest>? delegations) =>
        new(node.Name, node.Role, node.Model, node.Temperature, node.MaxTokens, node.MaxConcurrency, node.Priority, Clean(node.SystemPrompt),
            node.IsActive, skillIds, fallbackNodeId, delegations, node.AutoTesting, node.DirectRead, node.DirectReadPercent);
}

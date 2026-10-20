using AIThread.Common.DTOs;
using AIThread.Common.Enums;

namespace AIThread.Service.Implementations;

public sealed partial class SetupService
{
    // Creates or updates the preset node of a role. An unticked role deactivates its preset node (it is not deleted).
    // delegations null = leave them as they are. Null when nothing was done.
    private async Task<SetupPresetNodeDto?> PresetAsync(IReadOnlyList<NodeDto> existing, NodeRole role, string model, bool enabled,
        IReadOnlyList<ToolDelegationRequest>? delegations, List<SetupPresetNodeDto> results, CancellationToken cancellationToken)
    {
        var node = FindPreset(existing, role);
        if (!enabled)
        {
            if (node is not { IsActive: true }) return null;
            var deactivated = await nodeService.UpdateAsync(node.Id, ToRequest(node) with { IsActive = false }, cancellationToken);
            return Add(deactivated, false);
        }

        if (node is null)
        {
            var defaults = RoleDefaults[role];
            var created = await nodeService.CreateAsync(new SaveNodeRequest(PresetNames[role], role, model, defaults.Temperature, defaults.MaxTokens,
                defaults.MaxConcurrency, PresetPriority, null, true, null, null, delegations), cancellationToken);
            return Add(created, true);
        }

        var updated = await nodeService.UpdateAsync(node.Id, ToRequest(node) with { Model = model, IsActive = true, ToolDelegations = delegations },
            cancellationToken);
        return Add(updated, false);

        SetupPresetNodeDto? Add(NodeDto? saved, bool isNew)
        {
            if (saved is null) return null;
            var result = new SetupPresetNodeDto(saved, isNew);
            results.Add(result);
            return result;
        }
    }
}

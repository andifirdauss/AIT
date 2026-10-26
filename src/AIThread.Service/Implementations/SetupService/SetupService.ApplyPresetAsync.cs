using AIThread.Common.DTOs;
using AIThread.Common.Enums;
using AIThread.Common.Localization;
using AIThread.Service.Interfaces;
using AIThread.Service.Models;

namespace AIThread.Service.Implementations;

public sealed partial class SetupService
{
    // Planner (and reviewer, tester) on the strong model, the rest on the cheap one. Nodes added by hand are left alone; the
    // preset nodes get a lower priority number so they are the ones used.
    public async Task<SetupPresetResultDto> ApplyPresetAsync(SetupPresetRequest request, CancellationToken cancellationToken = default)
    {
        var strong = request.StrongModel?.Trim();
        if (string.IsNullOrEmpty(strong)) throw new AppValidationException(Lang.T("Pilih model kuat untuk planner.", "Choose a strong model for the planner."));
        var cheap = string.IsNullOrWhiteSpace(request.CheapModel) ? strong : request.CheapModel.Trim();

        var existing = await nodeService.GetAllAsync(cancellationToken);
        var results = new List<SetupPresetNodeDto>();

        // Tools node first: the planner's delegations point at it.
        var toolsNode = await PresetAsync(existing, NodeRole.ToolsNode, cheap, request.ToolsNode, null, results, cancellationToken);
        await PresetAsync(existing, NodeRole.Executor, cheap, true, null, results, cancellationToken);
        await PresetAsync(existing, NodeRole.Reviewer, strong, request.Reviewer, null, results, cancellationToken);
        await PresetAsync(existing, NodeRole.Tester, strong, request.Tester, null, results, cancellationToken);
        await PresetAsync(existing, NodeRole.Wrapper, cheap, true, null, results, cancellationToken);
        await PresetAsync(existing, NodeRole.ContextKeeper, cheap, request.ContextKeeper, null, results, cancellationToken);

        var presetToolsId = FindPreset(existing, NodeRole.ToolsNode)?.Id ?? toolsNode?.Node.Id;
        var delegations = PlannerDelegations(FindPreset(existing, NodeRole.Planner), presetToolsId, request.ToolsNode ? toolsNode?.Node.Id : null);
        await PresetAsync(existing, NodeRole.Planner, strong, true, delegations, results, cancellationToken);

        activityLog.Info("node", Lang.T($"Penyiapan: preset pipeline diterapkan — model kuat {strong}, model murah {cheap}.", $"Setup: pipeline preset applied — strong model {strong}, cheap model {cheap}."));
        return new SetupPresetResultDto(results);
    }
}

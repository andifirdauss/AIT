using AIThread.Common.DTOs;
using AIThread.Common.Enums;
using AIThread.Common.Localization;

namespace AIThread.Service.Implementations;

public sealed partial class SetupProfileService
{
    // What importing would change on an existing node, as short labels for the preview.
    private static List<string> NodeDiffs(NodeDto current, ResolvedNode target)
    {
        var node = target.Node;
        var diffs = new List<string>();
        if (!string.Equals(current.Model, node.Model, StringComparison.Ordinal)) diffs.Add($"model {current.Model} → {node.Model}");
        if (current.Temperature != node.Temperature) diffs.Add("temperature");
        if (current.MaxTokens != node.MaxTokens) diffs.Add("max token");
        if (current.MaxConcurrency != node.MaxConcurrency) diffs.Add(Lang.T("paralel", "parallel"));
        if (current.Priority != node.Priority) diffs.Add(Lang.T("prioritas", "priority"));
        if (!string.Equals(Clean(current.SystemPrompt), Clean(node.SystemPrompt), StringComparison.Ordinal)) diffs.Add(Lang.T("instruksi tambahan", "extra instructions"));
        if (current.IsActive != node.IsActive) diffs.Add(node.IsActive ? Lang.T("diaktifkan", "activated") : Lang.T("dinonaktifkan", "deactivated"));
        if (!string.Equals(current.FallbackNodeName, target.Fallback, StringComparison.OrdinalIgnoreCase)) diffs.Add(Lang.T("node cadangan", "fallback node"));

        var currentDelegations = (current.ToolDelegations ?? []).Select(d => $"{d.Tool}→{d.ToolsNodeName}".ToLowerInvariant()).Order();
        var targetDelegations = target.Delegations.Select(d => $"{d.Tool}→{d.ToolsNode}".ToLowerInvariant()).Order();
        if (current.Role == NodeRole.Planner && !currentDelegations.SequenceEqual(targetDelegations)) diffs.Add(Lang.T("delegasi tool", "tool delegation"));
        if (current.Role == NodeRole.Planner && node.AutoTesting is { } autoTesting && autoTesting != current.AutoTesting)
            diffs.Add(autoTesting ? Lang.T("Auto Testing dinyalakan", "Auto Testing turned on") : Lang.T("Auto Testing dimatikan", "Auto Testing turned off"));

        var currentSkills = (current.Skills ?? []).Select(s => s.Id).Order();
        if (!currentSkills.SequenceEqual(target.SkillIds.Order())) diffs.Add("skill");
        return diffs;
    }
}

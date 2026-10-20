using AiThread.Common.DTOs;
using AiThread.Common.Enums;

namespace AiThread.Service.Implementations;

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
        if (current.MaxConcurrency != node.MaxConcurrency) diffs.Add("paralel");
        if (current.Priority != node.Priority) diffs.Add("prioritas");
        if (!string.Equals(Clean(current.SystemPrompt), Clean(node.SystemPrompt), StringComparison.Ordinal)) diffs.Add("instruksi tambahan");
        if (current.IsActive != node.IsActive) diffs.Add(node.IsActive ? "diaktifkan" : "dinonaktifkan");
        if (!string.Equals(current.FallbackNodeName, target.Fallback, StringComparison.OrdinalIgnoreCase)) diffs.Add("node cadangan");

        var currentDelegations = (current.ToolDelegations ?? []).Select(d => $"{d.Tool}→{d.ToolsNodeName}".ToLowerInvariant()).Order();
        var targetDelegations = target.Delegations.Select(d => $"{d.Tool}→{d.ToolsNode}".ToLowerInvariant()).Order();
        if (current.Role == NodeRole.Planner && !currentDelegations.SequenceEqual(targetDelegations)) diffs.Add("delegasi tool");

        var currentSkills = (current.Skills ?? []).Select(s => s.Id).Order();
        if (!currentSkills.SequenceEqual(target.SkillIds.Order())) diffs.Add("skill");
        return diffs;
    }
}

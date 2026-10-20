using AIThread.Common.DTOs;

namespace AIThread.Service.Orchestration;

public sealed partial class PlannerToolRunner
{
    // The planner's memory map: labeled memories per module, with their layers and kinds.
    public static string FormatModuleMap(IReadOnlyList<MemoryModuleSummary> modules, int maxProject = 40, int maxKnowledge = 20)
    {
        var builder = new System.Text.StringBuilder();
        foreach (var module in modules.Where(m => !m.Knowledge).Take(maxProject))
        {
            var layers = string.Join(" · ", module.Layers.OrderByDescending(l => l.Value).Select(l => $"{l.Key} {l.Value}"));
            var kinds = string.Join(" · ", module.Kinds.OrderByDescending(k => k.Value).Select(k => $"{k.Key} {k.Value}"));
            builder.Append($"- {module.Module} ({module.Count}): {(layers.Length > 0 ? layers + " | " : string.Empty)}{kinds}\n");
        }
        var knowledge = modules.Where(m => m.Knowledge).Take(maxKnowledge).Select(m => $"{m.Module} ({m.Count})").ToList();
        if (knowledge.Count > 0) builder.Append("- Knowledge: ").Append(string.Join(" · ", knowledge)).Append('\n');
        return builder.ToString();
    }
}

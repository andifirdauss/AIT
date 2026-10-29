using System.Text.Json.Nodes;
using AIThread.Service.Models;

namespace AIThread.Service.Orchestration;

public sealed partial class ToolsNodeAgent
{
    // The findings of a report_findings call, when the model made one, with the line ranges it wants copied.
    private static (string Text, IReadOnlyList<ToolsNodeExcerpt> Excerpts)? Findings(IReadOnlyList<ToolCall> calls)
    {
        var report = calls.FirstOrDefault(c => c.Name == PlannerTools.ReportFindings);
        if (report is null) return null;
        var args = PlannerToolRunner.ParseArguments(report.Arguments, out _);
        if (args is null) return (report.Arguments, []);

        var excerpts = new List<ToolsNodeExcerpt>();
        if (args["excerpts"] is JsonArray items)
        {
            foreach (var item in items.OfType<JsonObject>())
            {
                var path = PlannerToolRunner.Str(item, "path");
                if (string.IsNullOrWhiteSpace(path) || PlannerToolRunner.Int(item, "startLine") is not { } start) continue;
                var end = PlannerToolRunner.Int(item, "endLine") ?? start;
                excerpts.Add(new ToolsNodeExcerpt(path.Trim(), Math.Max(1, Math.Min(start, end)), Math.Max(start, end)));
            }
        }
        return (PlannerToolRunner.Str(args, "findings") ?? string.Empty, excerpts);
    }
}

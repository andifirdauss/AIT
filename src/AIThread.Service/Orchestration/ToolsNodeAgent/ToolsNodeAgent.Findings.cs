using AIThread.Service.Models;

namespace AIThread.Service.Orchestration;

public sealed partial class ToolsNodeAgent
{
    // The findings of a report_findings call, when the model made one.
    private static string? Findings(IReadOnlyList<ToolCall> calls)
    {
        var report = calls.FirstOrDefault(c => c.Name == PlannerTools.ReportFindings);
        if (report is null) return null;
        var args = PlannerToolRunner.ParseArguments(report.Arguments, out _);
        return args is null ? report.Arguments : PlannerToolRunner.Str(args, "findings") ?? string.Empty;
    }
}

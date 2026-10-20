using System.Text;
using AiThread.Common.Enums;

namespace AiThread.Service.Orchestration;

public sealed partial class WrapperAgent
{
    private static string BuildReport(string userRequest, ThreadPlan plan, ExecutionSummary execution, string? verification)
    {
        var builder = new StringBuilder();
        builder.Append("# Developer request\n").Append(userRequest.Trim()).Append("\n\n");
        builder.Append("# Plan summary\n").Append(plan.Summary).Append("\n\n");

        if (plan.Contract.Count > 0)
        {
            builder.Append("# Contract\n");
            foreach (var item in plan.Contract) builder.Append($"- {item.Name}: {item.Value}\n");
            builder.Append('\n');
        }

        builder.Append($"# Results ({execution.Results.Count(r => r.Status == ThreadTaskStatus.Succeeded)}/{execution.Results.Count} succeeded)\n");
        foreach (var result in execution.Results)
        {
            var task = plan.Tasks.LastOrDefault(t => t.Id == result.TaskKey);
            builder.Append($"\n## {result.TaskKey} · {task?.Title ?? result.File} — {result.Status}\n");
            builder.Append($"File: {task?.Target ?? result.File} ({task?.Action ?? "edit"})\n");
            if (result.Summary is not null) builder.Append($"Summary: {result.Summary}\n");
            if (result.Error is not null) builder.Append($"Error: {result.Error}\n");
            if (result.Diff is not null)
            {
                var lines = result.Diff.Split('\n');
                builder.Append("```diff\n").Append(string.Join('\n', lines.Take(MaxDiffLinesPerTask)));
                if (lines.Length > MaxDiffLinesPerTask) builder.Append($"\n... ({lines.Length - MaxDiffLinesPerTask} more lines)");
                builder.Append("\n```\n");
            }
        }

        if (!string.IsNullOrWhiteSpace(verification))
            builder.Append("\n# Verification (build and review after the tasks above)\n").Append(verification.Trim()).Append('\n');

        return builder.ToString();
    }
}

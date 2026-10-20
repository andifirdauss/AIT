using System.Text;

namespace AIThread.Service.Orchestration;

public sealed partial class ReviewerAgent
{
    public static string BuildInput(string userRequest, ThreadPlan plan, IReadOnlyList<PlanTask> tasks, IReadOnlyList<TaskResult> results, string? buildReport)
    {
        var builder = new StringBuilder();
        builder.Append("# Developer request\n").Append(userRequest.Trim()).Append("\n\n");
        builder.Append("# Plan summary\n").Append(plan.Summary).Append("\n\n");

        builder.Append("# Contract\n");
        if (plan.Contract.Count == 0) builder.Append("(none)\n");
        foreach (var item in plan.Contract)
            builder.Append($"- {item.Name}: {item.Value}").Append(string.IsNullOrWhiteSpace(item.Note) ? string.Empty : $" ({item.Note})").Append('\n');
        builder.Append('\n');

        var byId = tasks.GroupBy(t => t.Id, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Last(), StringComparer.OrdinalIgnoreCase);
        builder.Append("# Tasks\n");
        foreach (var result in results)
        {
            byId.TryGetValue(result.TaskKey, out var task);
            builder.Append($"\n## {result.TaskKey} · {task?.Title ?? result.File} — {result.Status}\n");
            builder.Append($"File: {task?.Target ?? result.File} ({task?.Action ?? "edit"})\n");
            if (task is not null)
            {
                var instruction = task.Instruction.Trim();
                if (instruction.Length > MaxInstructionChars) instruction = instruction[..MaxInstructionChars] + " …";
                builder.Append("Instruction: ").Append(instruction).Append('\n');
            }
            if (result.Summary is not null) builder.Append($"Summary: {result.Summary}\n");
            if (result.Error is not null) builder.Append($"Error: {result.Error}\n");
            if (result.Diff is not null)
            {
                var lines = result.Diff.TrimEnd('\n').Split('\n');
                builder.Append("```diff\n").Append(string.Join('\n', lines.Take(MaxDiffLinesPerTask)));
                if (lines.Length > MaxDiffLinesPerTask) builder.Append($"\n... ({lines.Length - MaxDiffLinesPerTask} more lines)");
                builder.Append("\n```\n");
            }
        }

        builder.Append("\n# Build\n");
        builder.Append(string.IsNullOrWhiteSpace(buildReport) ? "AIT cannot build this project; review the diffs only.\n" : buildReport);
        return builder.ToString();
    }
}

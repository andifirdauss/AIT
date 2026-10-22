using System.Text;
using AIThread.Common.Enums;

namespace AIThread.Service.Orchestration;

public sealed partial class TesterAgent
{
    // The request, the plan, every change with its diff, and each failing test with its code and failure.
    public static string TriageBrief(UnitTestTriageInput input)
    {
        var builder = new StringBuilder();
        builder.Append("# Developer request\n").Append(input.Request.Prompt.Trim()).Append("\n\n");
        builder.Append("# Plan summary\n").Append(input.Plan.Summary).Append("\n\n");
        if (input.Plan.Contract.Count > 0)
        {
            builder.Append("# Contract\n");
            foreach (var item in input.Plan.Contract) builder.Append($"- {item.Name}: {item.Value}\n");
            builder.Append('\n');
        }

        var byId = input.Tasks.GroupBy(t => t.Id, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Last(), StringComparer.OrdinalIgnoreCase);
        builder.Append("# Changes\n");
        foreach (var result in input.Results.Where(r => r.Status == ThreadTaskStatus.Succeeded))
        {
            byId.TryGetValue(result.TaskKey, out var task);
            builder.Append($"\n## {result.TaskKey} · {task?.Target ?? result.File}\n");
            if (result.Diff is null) continue;
            var lines = result.Diff.TrimEnd('\n').Split('\n');
            builder.Append("```diff\n").Append(string.Join('\n', lines.Take(MaxTriageDiffLines)));
            if (lines.Length > MaxTriageDiffLines) builder.Append($"\n... ({lines.Length - MaxTriageDiffLines} more lines)");
            builder.Append("\n```\n");
        }

        builder.Append("\n# Failing unit tests\n");
        foreach (var failure in input.Failures)
        {
            builder.Append($"\n## {failure.Test} · {failure.Title}\n");
            builder.Append($"Decision: {failure.Decision}\nTest file: {failure.TestFile}\n");
            if (failure.Purpose is not null) builder.Append($"Purpose: {failure.Purpose}\n");
            if (failure.Source is not null) builder.Append("Test code:\n```\n").Append(failure.Source.TrimEnd()).Append("\n```\n");
            builder.Append("Failure:\n```\n").Append((failure.Message ?? "(no message)").TrimEnd()).Append("\n```\n");
        }
        return builder.ToString();
    }
}

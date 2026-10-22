using System.Text;
using AIThread.Common.Enums;
using AIThread.Service.Testing;

namespace AIThread.Service.Orchestration;

public sealed partial class TesterAgent
{
    // What the tester knows about the change: the request, the plan, the diffs and the planner's test section.
    public static string Brief(TestingInput input)
    {
        var plan = input.Plan;
        var test = plan.Test!;
        var builder = new StringBuilder();
        builder.Append("# Project\n").Append(input.Project.Name);
        if (input.Project.Stacks.Count > 0) builder.Append($" ({string.Join(", ", input.Project.Stacks)})");
        builder.Append("\n\n# Developer request\n").Append(input.Request.Prompt.Trim()).Append("\n\n");
        builder.Append("# Plan summary\n").Append(plan.Summary).Append("\n\n");

        if (plan.Contract.Count > 0)
        {
            builder.Append("# Contract\n");
            foreach (var item in plan.Contract)
                builder.Append($"- {item.Name}: {item.Value}").Append(string.IsNullOrWhiteSpace(item.Note) ? string.Empty : $" ({item.Note})").Append('\n');
            builder.Append('\n');
        }

        var byId = plan.Tasks.GroupBy(t => t.Id, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Last(), StringComparer.OrdinalIgnoreCase);
        builder.Append("# Changes\n");
        if (input.Execution.Results.Count == 0)
            builder.Append("None: nothing was changed in this thread. The developer asked to test the existing behaviour.\n");
        foreach (var result in input.Execution.Results)
        {
            byId.TryGetValue(result.TaskKey, out var task);
            builder.Append($"\n## {result.TaskKey} · {task?.Title ?? result.File} — {result.Status}\n");
            builder.Append($"File: {task?.Target ?? result.File}\n");
            if (result.Status != ThreadTaskStatus.Succeeded)
            {
                builder.Append("Not applied").Append(result.Error is null ? string.Empty : $": {result.Error}").Append('\n');
                continue;
            }
            if (task is not null && result.Diff is null)
            {
                var instruction = task.Instruction.Trim();
                builder.Append("Instruction: ").Append(instruction.Length > MaxInstructionChars ? instruction[..MaxInstructionChars] + " …" : instruction).Append('\n');
            }
            if (result.Summary is not null) builder.Append($"Summary: {result.Summary}\n");
            if (result.Diff is not null)
            {
                var lines = result.Diff.TrimEnd('\n').Split('\n');
                builder.Append("```diff\n").Append(string.Join('\n', lines.Take(MaxDiffLinesPerTask)));
                if (lines.Length > MaxDiffLinesPerTask) builder.Append($"\n... ({lines.Length - MaxDiffLinesPerTask} more lines)");
                builder.Append("\n```\n");
            }
        }

        builder.Append("\n# Login\n");
        if (input.Login is { } login)
        {
            builder.Append($"Username: {TestLogin.UsernamePlaceholder} (= {login.Username})\n");
            builder.Append($"Password: {TestLogin.PasswordPlaceholder}\n");
            builder.Append("Write these placeholders exactly as shown; AIT replaces them with the developer's login when the tool runs.\n");
        }
        else
        {
            builder.Append(input.CanAskLogin
                ? "None given here. Use credentials from your additional instructions if they give any; otherwise, when the app asks for a login, call ask_login.\n"
                : "None given here. Use credentials from your additional instructions if they give any; otherwise mark scenarios that need a login \"blocked\" (the developer cannot be asked now).\n");
        }

        builder.Append("\n# Test plan\n");
        builder.Append($"Level: {test.Level}").Append(test.Level == TestPlan.Ui
            ? " — check the pages in the browser; http_request is available too.\n"
            : " — call the endpoints with http_request.\n");
        builder.Append($"Base URL: {test.BaseUrl}\n");
        if (test.Notes is not null) builder.Append($"Notes: {test.Notes}\n");
        for (var i = 0; i < test.Scenarios.Count; i++)
        {
            var scenario = test.Scenarios[i];
            builder.Append($"\n## Scenario {i + 1}: {scenario.Title}\n");
            if (scenario.Target is not null) builder.Append($"Start at: {scenario.Target}\n");
            if (scenario.Steps.Count > 0)
            {
                builder.Append("Steps:\n");
                for (var s = 0; s < scenario.Steps.Count; s++) builder.Append($"{s + 1}. {scenario.Steps[s]}\n");
            }
            builder.Append($"Expect: {scenario.Expect}\n");
        }
        return builder.ToString();
    }
}

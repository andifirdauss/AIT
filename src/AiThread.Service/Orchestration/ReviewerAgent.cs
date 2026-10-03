using System.Text;
using System.Text.Json.Nodes;
using AiThread.Common.Entities;
using AiThread.Service.Interfaces;
using AiThread.Service.Models;

namespace AiThread.Service.Orchestration;

public sealed record ReviewIssue(string File, string? Anchor, string Problem, string Fix);

// Approved = nothing to fix. Error is set when the reviewer could not be asked or answered unreadably; the review
// then counts as not done, never as a reason to change code.
public sealed record ReviewResult(bool Approved, string? Summary, IReadOnlyList<ReviewIssue> Issues, int Tokens, string? Error = null)
{
    public static ReviewResult Failed(string error) => new(true, null, [], 0, error);
}

// One call: the request, the plan and contract, every task's diff and the build result in; the problems that break
// the request out, each phrased as an instruction an executor can apply to one file.
public sealed class ReviewerAgent(INineRouterService router, NodeConcurrencyLimiter limiter, NodePromptBuilder prompts)
{
    public const int MaxIssues = 20;
    private const int MaxDiffLinesPerTask = 150;
    private const int MaxInstructionChars = 1500;

    public async Task<ReviewResult> ReviewAsync(
        AiNode node,
        string userRequest,
        ThreadPlan plan,
        IReadOnlyList<PlanTask> tasks,
        IReadOnlyList<TaskResult> results,
        string? buildReport,
        CancellationToken cancellationToken)
    {
        var systemPrompt = await prompts.BuildAsync(AgentPrompts.Reviewer, node, cancellationToken);
        ChatCompletionResult result;
        using (await limiter.AcquireAsync(node, cancellationToken))
        {
            result = await router.ChatAsync(new ChatCompletionRequest
            {
                Model = node.Model,
                Messages =
                [
                    ChatMessages.System(systemPrompt),
                    ChatMessages.User(BuildInput(userRequest, plan, tasks, results, buildReport))
                ],
                Temperature = node.Temperature ?? 0.1,
                MaxTokens = node.MaxTokens
            }, cancellationToken: cancellationToken);
        }

        return Parse(result.Content) with { Tokens = result.TotalTokens };
    }

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
            builder.Append($"File: {result.File} ({task?.Action ?? "edit"})\n");
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
        builder.Append(string.IsNullOrWhiteSpace(buildReport) ? "AiThread cannot build this project; review the diffs only.\n" : buildReport);
        return builder.ToString();
    }

    public static ReviewResult Parse(string content)
    {
        var start = content.IndexOf('{');
        var end = content.LastIndexOf('}');
        if (start < 0 || end <= start) return ReviewResult.Failed("Reviewer did not answer with JSON.");

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(content[start..(end + 1)]);
        }
        catch (System.Text.Json.JsonException ex)
        {
            return ReviewResult.Failed($"Reviewer answered with invalid JSON ({ex.Message}).");
        }

        var issues = new List<ReviewIssue>();
        foreach (var item in root?["issues"] as JsonArray ?? [])
        {
            var file = Text(item?["file"])?.Trim().Trim('`');
            var problem = Text(item?["problem"])?.Trim();
            if (string.IsNullOrWhiteSpace(file) || string.IsNullOrWhiteSpace(problem)) continue;

            var fix = Text(item?["fix"])?.Trim();
            var anchor = Text(item?["anchor"])?.Trim();
            issues.Add(new ReviewIssue(file, string.IsNullOrWhiteSpace(anchor) ? null : anchor, problem, string.IsNullOrWhiteSpace(fix) ? problem : fix));
            if (issues.Count >= MaxIssues) break;
        }

        // The issue list decides: "approved": false without an issue gives an executor nothing to do.
        return new ReviewResult(issues.Count == 0, Text(root?["summary"])?.Trim(), issues, 0);
    }

    private static string? Text(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
}
